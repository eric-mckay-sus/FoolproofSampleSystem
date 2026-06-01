// <copyright file="PrintSample.cs" company="Stanley Electric US Co. Inc.">
// Copyright (c) 2026 Stanley Electric US Co. Inc. Licensed under the MIT License.
// </copyright>

namespace PrintLabel;

using System.Text;
using System.Net.Sockets;
using Microsoft.Data.SqlClient;

using InterProcessIO;

/// <summary>
/// Interface to define the source of a sample label.
/// </summary>
public interface ISampleLabelSource
{
    /// <summary>
    /// Verifies that a particular sample ID exists in the sample data source.
    /// </summary>
    /// <param name="sampleId">The sample ID to validate.</param>
    /// <returns>Whether <paramref name="sampleId"/> exists in the sample data source.</returns>
    Task<bool> SampleExistsAsync(int? sampleId);

    /// <summary>
    /// Queries the sample data source by target ID and collects the info necessary to fill out a sample label.
    /// </summary>
    /// <param name="sampleId">The ID of the sample for which to fetch label information.</param>
    /// <returns>A string array containing the field data for the label (from the data source).</returns>
    Task<string[]> GetLabelFieldsAsync(int? sampleId);
}

/// <summary>
/// SQL Server implementation of <see cref="ISampleLabelSource"/>.
/// </summary>
public class SqlSampleLabelSource : ISampleLabelSource
{
    /// <summary>
    /// Verifies that the DB contains a sample with ID = <paramref name="sampleId"/>.
    /// </summary>
    /// <param name="sampleId"><inheritdoc path="/param[@name='sampleId']"/></param>
    /// <returns>Whether the DB contains a sample with ID = <paramref name="sampleId"/>.</returns>
    public async Task<bool> SampleExistsAsync(int? sampleId)
    {
        // Auto-increment fields are never negative
        if (sampleId < 0)
        {
            return false;
        }

        using SqlConnection conn = new (Config.GetConnectionString());
        await conn.OpenAsync();

        string sql = @"
            SELECT COUNT(*) FROM dbo.Samples
                   WHERE sampleID LIKE @sampleId";

        using SqlCommand cmd = new (sql, conn);
        cmd.Parameters.AddWithValue("@sampleId", sampleId);

        int count = (int)(await cmd.ExecuteScalarAsync() ?? 0);

        return count > 0;
    }

    /// <summary>
    /// Fetches label information for the sample with ID = <paramref name="sampleId"/> from the DB.
    /// </summary>
    /// <param name="sampleId"><inheritdoc path="/param[@name='sampleId']"/></param>
    /// <returns>A string array with the label information selected from the database.</returns>
    public async Task<string[]> GetLabelFieldsAsync(int? sampleId)
    {
        if (sampleId == null)
        {
            return [];
        }

        using SqlConnection conn = new (Config.GetConnectionString());
        await conn.OpenAsync();

        string[] fields = new string[10];

        // Define the query to pull fields required by the ZPL template
        string query = @"
            SELECT
                dummySampleNum, model, rank,
                workCenterCode, iteration, creationDate,
                failureMode, location, creatorNum
            FROM Samples
            WHERE sampleID = @id";

        using (SqlCommand cmd = new (query, conn))
        {
            cmd.Parameters.AddWithValue("@id", sampleId);

            using (SqlDataReader reader = await cmd.ExecuteReaderAsync())
            {
                if (await reader.ReadAsync())
                {
                    // Helper to cast nulls to the empty string
                    static string NullToEmpty(object value) => value?.ToString() ?? string.Empty;

                    // Map database columns to ZPL template indices
                    fields[0] = NullToEmpty(reader["dummySampleNum"]);
                    fields[1] = NullToEmpty(reader["model"]);
                    fields[2] = NullToEmpty(reader["rank"]);
                    fields[3] = NullToEmpty(reader["workCenterCode"]);
                    fields[4] = NullToEmpty(sampleId);
                    fields[5] = NullToEmpty(reader["iteration"]);
                    fields[6] = NullToEmpty(((DateTime)reader["creationDate"]).ToString("MM/dd/yyyy"));
                    fields[7] = NullToEmpty(reader["failureMode"]);
                    fields[8] = NullToEmpty(reader["location"]);
                    fields[9] = NullToEmpty(reader["creatorNum"]);
                }
            }
        }

        return fields;
    }
}

/// <summary>
/// Defines methods used to print a sample.
/// </summary>
public partial class ZebraPrintFlow
{
    /// <summary>
    /// Prompts for and validates the information necessary for a print command (sample ID and print DPI).
    /// </summary>
    /// <param name="printCmd">The <see cref="ZplCommand"/> in which to assign the print path.</param>
    /// <returns>A Task representing that the sample ID and print path have been provided.</returns>
    public async Task PromptPrint(ZplCommand printCmd)
    {
        string? error;
        string idString;
        int sampleId;
        do
        {
            error = null; // Don't persist error from last iteration or from upload path prompt
            idString = await this.input.GetInputAsync(new ("Please enter the ID of the sample to be printed"), error);

            // Set error message if applicable (cheapest check first, first hit holds)
            if (!int.TryParse(idString, out sampleId))
            {
                error = $"Sample ID '{idString}' is not an integer. Please try again.";
            }

            // Only check sample ID existence if we know it's an integer (thus could theoretically be in the DB), otherwise this is an unnecessary expense
            else if (!await this.sampleSource.SampleExistsAsync(sampleId))
            {
                error = $"Sample ID '{idString}' not in the sample database. Please choose another sample ID.";
            }
        }
        while (error != null);

        printCmd.SampleId = sampleId;

        // Template path will be generated from the target DPI
        string potentialDpi;
        int printDpi;
        do
        {
            error = null; // Don't persist error from last iteration or from sample ID prompt
            potentialDpi = await this.input.GetInputAsync(new ("Please enter the DPI of the target printer (or just press ENTER to use the config file default): "), error);

            // Use default and end prompting immediately on empty input
            if (potentialDpi.Equals(string.Empty))
            {
                printCmd.PrintDpi = Config.PrinterDpi;
                return;
            }

            // Set error message if applicable (first one holds)
            if (!int.TryParse(potentialDpi, out printDpi))
            {
                error = $"DPI '{potentialDpi}' is not an integer. Please try again.";
            }
            else if (!Config.DpiToTemplatePath.ContainsKey(printDpi))
            {
                error = $"There is no configured option to print at {potentialDpi} DPI. Please try again.";
            }
        }
        while (error != null);

        printCmd.PrintDpi = printDpi; // Could also store print template path in ZplCommand, but that wouldn't change the fact that we need to check the DPI in PrintAsync for direct calls to ExecuteAsync
    }

    /// <summary>
    /// Validates and prints file from the printer watching for data from <paramref name="stream"/>.
    /// </summary>
    /// <param name="printCmd">The <see cref="ZplCommand"/> containing the print path.</param>
    /// <param name="stream">The <see cref="Stream"/> to the printer.</param>
    /// <returns>A Task representing that the print command has been issued (or been terminated).</returns>
    public async Task PrintAsync(ZplCommand printCmd, Stream stream)
    {
        string templatePath;

        if (Config.DpiToTemplatePath.TryGetValue(printCmd.PrintDpi, out string? path))
        {
            templatePath = path;
        }
        else
        {
            await this.Report($"There is no configured option to print at {printCmd.PrintDpi} DPI. Cancelling print...");
            return;
        }

        string[] fields = await this.sampleSource.GetLabelFieldsAsync(printCmd.SampleId);

        // SampleMapFromId only returns empty when the sample ID couldn't be found
        // Could technically perform an existence check beforehand, but this is equivalent and requires 1 fewer DB hit
        if (fields.Length == 0)
        {
            await this.Report($"{printCmd.SampleId} is not the ID of a sample in the database. Cancelling print...");
            return;
        }

        string toUpload = await this.BuildZplCommand(templatePath, fields);

        if (!string.IsNullOrEmpty(toUpload))
        {
            // Stream loaded template to printer (printer executes immediately)
            await stream.WriteAsync(Encoding.UTF8.GetBytes(toUpload));

            await this.Report("Sent print command to printer. Print should begin shortly.", ReportLevel.SUCCESS);
        }
        else
        {
            await this.Report("There was an error accessing your file.");
        }
    }

    /// <summary>
    /// Builds the ZPL payload to be sent to the printer, without establishing printer connection.
    /// Handles error reporting internally and returns empty string in case of error.
    /// </summary>
    /// <param name="templatePath">The path to the ZPL template file.</param>
    /// <param name="fields">The values to be used to fill template placeholders.</param>
    /// <returns>The ZPL string ready to print.</returns>
    public async Task<string> BuildZplCommand(string templatePath, string[] fields)
    {
        FileInfo templateInfo = new (templatePath);
        int kbSize = Convert.ToInt32(templateInfo.Length / 1024);

        if (kbSize > Config.KbLimit)
        {
            await this.Report($"{templatePath} exceeds the size limit of {Config.KbLimit}KB. Canceling upload...", ReportLevel.ERROR);
            return string.Empty;
        }

        string toReturn;
        try
        {
            toReturn = await File.ReadAllTextAsync(templatePath);
            toReturn = string.Format(toReturn, fields);
            return toReturn;
        }
        catch (FileNotFoundException)
        {
            await this.Report($"Your template ZPL file could not be found at {templatePath} (inferred from your DPI). Please ensure the file has not moved or been renamed.", ReportLevel.ERROR);
        }
        catch (DirectoryNotFoundException)
        {
            await this.Report($"Your template ZPL file could not be found at {templatePath} (inferred from your DPI). Please ensure the path to this file exists.", ReportLevel.ERROR);
        }
        catch (UnauthorizedAccessException)
        {
            await this.Report($"Your template ZPL file could not be accessed at {templatePath} (inferred from your DPI). Please ensure you have permission to read this file.", ReportLevel.ERROR);
        }
        catch (IOException)
        {
            await this.Report($"Your template ZPL file could not be accessed at {templatePath} (inferred from your DPI). If it is open, please close it so the printer may read it.", ReportLevel.ERROR);
        }

        return string.Empty;
    }
}
