// <copyright file="PrintSample.cs" company="Stanley Electric US Co. Inc.">
// Copyright (c) 2026 Stanley Electric US Co. Inc. Licensed under the MIT License.
// </copyright>

namespace PrintLabel;

using InterProcessIO;
using Microsoft.Data.SqlClient;
using System.Text;
using System.Net.Sockets;

/// <summary>
/// Defines methods used to print a sample.
/// </summary>
public partial class ZebraUploadPrint
{
    /// <summary>
    /// Prompts for and validates the information necessary for a print command (sample ID and print path).
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
            idString = await this.input.GetInputAsync(new ("Please enter sample ID to be printed"), error);

            // Set error message if applicable (cheapest check first, first hit holds)
            if (!int.TryParse(idString, out sampleId))
            {
                error = $"Sample ID '{idString}' is not an integer. Please try again.";
            }

            // Only check sample ID existence if we know it's an integer (thus could theoretically be in the DB), otherwise this is an unnecessary expense
            else if (!await ValidateSample(sampleId))
            {
                error = $"Sample ID '{idString}' not in the sample database. Please choose another sample ID.";
            }
        }
        while (error != null);

        printCmd.SampleId = sampleId;

        // TODO want to ask for explicit filepath from console, but offer file input to Blazor. New method in IInputProvider?
        // User probably wants to stick with the config file option for print the majority of the time; how to lean toward that?
        string potentialPrintPath;
        do
        {
            error = null; // Don't persist error from last iteration or from sample ID prompt
            potentialPrintPath = await this.input.GetInputAsync(new ("Please enter the filename of the template ZPL to print (or just press ENTER to use the config file default): "), error);

            // Set error message if applicable (first one holds)
            if (!Path.GetExtension(potentialPrintPath).Equals(".zpl"))
            {
                error = $"File {potentialPrintPath} is not a ZPL file. Please try again";
            }
            else if (!(potentialPrintPath.StartsWith("R:") || potentialPrintPath.StartsWith("E:")))
            {
                error = $"File '{potentialPrintPath}' must be on the R or E drive. Please try again.";
            }
        }
        while (error != null);

        // Leave on default if empty
        if (!string.IsNullOrWhiteSpace(potentialPrintPath))
        {
            printCmd.PrintPath = potentialPrintPath;
        }
    }

    /// <summary>
    /// Validates and prints file from the printer watching for data from <paramref name="stream"/>.
    /// </summary>
    /// <param name="printCmd">The <see cref="ZplCommand"/> containing the print path.</param>
    /// <param name="stream">The <see cref="NetworkStream"/> to the printer.</param>
    /// <returns>A Task representing that the print command has been issued (or been terminated).</returns>
    public async Task PrintAsync(ZplCommand printCmd, NetworkStream stream)
    {
        Dictionary<int, string> fields;

        using (SqlConnection sqlConn = new (Config.GetConnectionString()))
        {
            // Map ^FN numbers to values
            fields = await SampleMapFromId(printCmd.SampleId, sqlConn);
        }

        // SampleMapFromId only returns empty when the sample ID couldn't be found
        if (fields.Count == 0)
        {
            await this.Report($"{printCmd.SampleId} is not the ID of a sample in the database. Cancelling print...");
        }

        StringBuilder sb = new ();

        // Recall and print
        sb.Append($"^XA^XF{printCmd.PrintPath}");
        foreach (KeyValuePair<int, string> entry in fields)
        {
            sb.Append($"^FN{entry.Key}^FD{entry.Value}^FS");
        }

        sb.Append("^XZ");

        await stream.WriteAsync(Encoding.UTF8.GetBytes(sb.ToString()));

        await this.Report("Sent print command to printer. Print should begin shortly.", ReportLevel.SUCCESS);
    }

    /// <summary>
    /// Verifies that a particular sample ID exists in the sample database.
    /// </summary>
    /// <param name="toValidate">The sample ID to validate.</param>
    /// <returns>Whether <paramref name="toValidate"/> exists in the sample database.</returns>
    private static async Task<bool> ValidateSample(int? toValidate)
    {
        // Auto-increment fields are never negative
        if (toValidate < 0)
        {
            return false;
        }

        using SqlConnection conn = new (Config.GetConnectionString());
        await conn.OpenAsync();

        string sql = @"
            SELECT COUNT(*) FROM dbo.Samples
                   WHERE sampleID LIKE @sampleId";

        using SqlCommand cmd = new (sql, conn);
        cmd.Parameters.AddWithValue("@sampleId", toValidate);

        int count = (int)(await cmd.ExecuteScalarAsync() ?? 0);

        return count > 0;
    }

    /// <summary>
    /// Queries the sample table by target ID and collects the info necessary to fill out a sample label.
    /// </summary>
    /// <param name="id">The sample serial number.</param>
    /// <param name="conn">The connection to the SQL database.</param>
    /// <returns>A dictionary mapping field numbers (for the ZPL template) to field data (from the database).</returns>
    private static async Task<Dictionary<int, string>> SampleMapFromId(int? id, SqlConnection conn)
    {
        if (id == null)
        {
            return [];
        }

        if (conn.State != System.Data.ConnectionState.Open)
        {
            await conn.OpenAsync();
        }

        var fieldMap = new Dictionary<int, string>();

        // Define the query to pull fields required by the ZPL template
        string query = @"
            SELECT
                dummySampleNum, model, rank,
                workCenterCode, iteration, creationDate,
                failureMode, location, creatorName
            FROM Samples
            WHERE sampleID = @id";

        using (SqlCommand cmd = new (query, conn))
        {
            cmd.Parameters.AddWithValue("@id", id);

            using (SqlDataReader reader = await cmd.ExecuteReaderAsync())
            {
                if (await reader.ReadAsync())
                {
                    // Helper to format strings with the ZPL centering suffix
                    static string Format(object value) => $"{value?.ToString() ?? string.Empty}\\&";

                    // Map database columns to ZPL ^FN indices
                    fieldMap.Add(1,  Format(reader["dummySampleNum"]));
                    fieldMap.Add(2,  Format(reader["model"]));
                    fieldMap.Add(3,  Format(reader["rank"]));
                    fieldMap.Add(4,  Format(id));
                    fieldMap.Add(5,  Format(reader["workCenterCode"]));
                    fieldMap.Add(6,  Format(reader["iteration"]));
                    fieldMap.Add(7,  Format(((DateTime)reader["creationDate"]).ToString("MM/dd/yyyy")));
                    fieldMap.Add(8,  Format(reader["failureMode"]));
                    fieldMap.Add(9,  Format(reader["location"]));
                    fieldMap.Add(10, Format(reader["creatorName"]));
                }
            }
        }

        return fieldMap;
    }
}
