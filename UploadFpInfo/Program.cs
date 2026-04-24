// <copyright file="Program.cs" company="Stanley Electric US Co. Inc.">
// Copyright (c) 2026 Stanley Electric US Co. Inc. Licensed under the MIT License.
// </copyright>

namespace UploadFpInfo;

using System.Data;
using Microsoft.Data.SqlClient;
using NPOI.HSSF.UserModel; // for older XLS files
using NPOI.SS.UserModel; // for generic spreadsheet manipulation
using NPOI.XSSF.UserModel; // for newer XLSX files
using static Path;

using static FPUploadUtilities; // static allows its methods to be accessed later without qualification
using InterProcessIO;

/// <summary>
/// Consolidates the parse/upload process for foolproof dummy sample sheets
/// The model to line database must be populated for insertion validation to succeed.
/// </summary>
public class FPSheetUploader
{
    /// <summary>
    /// Determines where user input comes from.
    /// </summary>
    private readonly IInputProvider input;

    /// <summary>
    /// Determines where/how program output is displayed.
    /// </summary>
    private readonly IOutputProvider output;

    /// <summary>
    /// Initializes a new instance of the <see cref="FPSheetUploader"/> class.
    /// By default, uses the console for input and output.
    /// </summary>
    public FPSheetUploader()
    {
        this.input = new ConsoleInputProvider();
        this.output = new ConsoleReporter();
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="FPSheetUploader"/> class, using the specified input and output providers.
    /// </summary>
    /// <param name="inputProvider">The instance of IInputProvider to be used to get input regarding FP sheet details.</param>
    /// <param name="outputProvider">The instance of IReportOutputProvider to be used for displaying program results.</param>
    public FPSheetUploader(IInputProvider inputProvider, IOutputProvider outputProvider)
    {
        this.input = inputProvider;
        this.output = outputProvider;
    }

    /// <summary>
    /// Main entry point: Instantiate an uploader using the default constructor to
    /// print to the console, then delegate the actual ETL process to the uploader.
    /// </summary>
    /// <param name="args">Command line arguments, accepts an optional file path.</param>
    /// <returns>A Task representing the completion of the program.</returns>
    public static async Task Main(string[] args)
    {
        // If there was an input location argument, pass it along (no validation here)
        string? potentialFile = null;
        if (args.Length > 0)
        {
            potentialFile = args[0];
        }

        // Exit static by creating an uploader
        FPSheetUploader uploader = new ();

        // Then give it the green light
        await uploader.ExecuteAsync(potentialFile);
    }

    /// <summary>
    /// Identifies input location and whether it is a folder/file, then delegates to the batch/file handler.
    /// Recommended entry point for other programs which use this one.
    /// </summary>
    /// <param name="filename">An optional file path to override the one found in config.</param>
    /// <returns>A Task representing the upload status.</returns>
    public async Task<UploadResult> ExecuteAsync(string? filename = null)
    {
        // Main only checked that there was an argument, now we validate
        string path = Config.GetInputLocation(isFP: true);
        if (string.IsNullOrWhiteSpace(filename))
        {
            await this.Report($"No file specified. Defaulting to config file input location ({path})\n");
        }
        else if (!Path.Exists(path))
        {
            await this.Report($"Path '{filename}' is not a valid directory or Excel file. Using Config default ({path}).\n", ReportLevel.WARNING);
        }
        else
        {
            path = filename;
        }

        bool containsDuplicate = false;
        bool containsMiscError = false;

        try
        {
            if (Directory.Exists(path))
            {
                (containsDuplicate, containsMiscError) = await this.RunBatch(path);
            }
            else if (File.Exists(path) && IsExcelFile(path))
            {
                (containsDuplicate, containsMiscError) = await this.ProcessFile(path);
            }

            // should never reach here unless the file is somehow deleted during the upload
            else
            {
                await this.Report($"Could not find {path}. Please verify the path is correct, then try again.");
                return UploadResult.ErroredOut;
            }

            // Declare the upload as complete when the batch/file finishes
            await this.output.ReportProgress(ProgressEvent.UploadComplete);

            if (containsDuplicate)
            {
                string[] duplicateNames = this.output.BatchResults.Where(fr => fr.hadDuplicates).Select(fr => GetFileName(fr.file)).ToArray();
                string report = string.Join("\n\t", duplicateNames);
                await this.Report($"The following files contain duplicate entries:\n\t{report}\nIf you wish to update, do so manually. Otherwise, no action is required.", ReportLevel.WARNING);
            }

            if (containsMiscError)
            {
                string[] miscNames = this.output.BatchResults.Where(fr => fr.hadErrors).Select(fr => GetFileName(fr.file)).ToArray();
                string report = string.Join("\n\t", miscNames);
                await this.Report($"The following files contain miscellaneous errors:\n{report}\nPlease investigate them to verify why they could not upload.", ReportLevel.ERROR);
            }

            if (containsDuplicate || containsMiscError)
            {
                return UploadResult.CompleteWithErrors;
            }
            else
            {
                return UploadResult.Complete;
            }
        }
        catch (FormatException f)
        {
            await this.Report($"Formatting error: {f.Message}", ReportLevel.ERROR);
            return UploadResult.ErroredOut;
        }
        catch (Exception ex)
        {
            await this.Report($"Fatal error: {ex.Message}", ReportLevel.ERROR);
            return UploadResult.ErroredOut;
        }
    }

    /// <summary>
    /// Verifies that a particular model exists in the model to line (MTL) database.
    /// </summary>
    /// <param name="toValidate">The model name to validate.</param>
    /// <returns>Whether <paramref name="toValidate"/> exists in the MTL database.</returns>
    private static async Task<bool> ValidateModel(string? toValidate)
    {
        if (string.IsNullOrWhiteSpace(toValidate))
        {
            return false;
        }

        using SqlConnection conn = new (Config.GetConnectionString());
        await conn.OpenAsync();

        string sql = @"
            SELECT COUNT(*) FROM dbo.ModelToLine
                   WHERE shortDesc LIKE @model";

        using SqlCommand cmd = new (sql, conn);
        cmd.Parameters.AddWithValue("@model", toValidate);

        int count = (int)(await cmd.ExecuteScalarAsync() ?? 0);

        return count > 0;
    }

    private static async Task ExecuteBulkCopy(DataTable dt, SqlConnection conn)
    {
        using SqlBulkCopy bulkCopy = new (conn);
        bulkCopy.DestinationTableName = "dbo.FoolproofInfo";

        // Map DataTable columns to DB columns
        bulkCopy.ColumnMappings.Add("model", "model");
        bulkCopy.ColumnMappings.Add("revision", "revision");
        bulkCopy.ColumnMappings.Add("issueDate", "issueDate");
        bulkCopy.ColumnMappings.Add("issuer", "issuer");
        bulkCopy.ColumnMappings.Add("failureMode", "failureMode");
        bulkCopy.ColumnMappings.Add("rank", "rank");
        bulkCopy.ColumnMappings.Add("location", "location");
        bulkCopy.ColumnMappings.Add("dummySampleNum", "dummySampleNum");

        await bulkCopy.WriteToServerAsync(dt);
    }

    /// <summary>
    /// Asynchronously writes the input DataRow's contents to the FP info table.
    /// Only use this method after attempting (and failing) a bulk copy.
    /// </summary>
    /// <param name="dr">The DataRow whose contents will be written to the server.</param>
    /// <param name="conn">The open SQL connection to be used in the SqlCommand.</param>
    /// <returns>A Task representing the completion (or failure) of the insertion.</returns>
    private static async Task WriteRowToDatabase(DataRow dr, SqlConnection conn)
    {
        string sql = @"
            INSERT INTO dbo.FoolproofInfo
            (model, revision, issueDate, issuer, failureMode, rank, location, dummySampleNum)
            VALUES
            (@model, @revision, @issueDate, @issuer, @failureMode, @rank, @location, @dummySampleNum)";

        using SqlCommand cmd = new (sql, conn);

        // Mapping parameters from the DataRow
        cmd.Parameters.AddWithValue("@model", dr["model"]);
        cmd.Parameters.AddWithValue("@revision", dr["revision"]);
        cmd.Parameters.AddWithValue("@issueDate", dr["issueDate"]);
        cmd.Parameters.AddWithValue("@issuer", dr["issuer"]);
        cmd.Parameters.AddWithValue("@failureMode", dr["failureMode"]);
        cmd.Parameters.AddWithValue("@rank", dr["rank"]);
        cmd.Parameters.AddWithValue("@location", dr["location"]);
        cmd.Parameters.AddWithValue("@dummySampleNum", dr["dummySampleNum"]);

        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Processes a batch of FP info files.
    /// </summary>
    /// <returns>An tuple representing whether the batch contains a file that 1) contains PK collision(s) and 2) has a miscellaneous error.</returns>
    private async Task<(bool, bool)> RunBatch(string directoryPath)
    {
        DirectoryInfo inputDir = new (directoryPath);

        FileInfo[] files = inputDir.GetFiles("*.xlsx")
                            .Concat(inputDir.GetFiles("*.xls"))
                            .OrderBy(f => f.Name)
                            .ToArray();

        if (files.Length == 0)
        {
            await this.Report("No Excel files found.", ReportLevel.ERROR);
            return (false, false);
        }

        await this.Report($"Found {files.Length} files. Starting upload to database...\n");

        bool currentContainsDuplicate = false;
        bool currentContainsMisc = false;
        bool batchContainsDuplicate = false;
        bool batchContainsMisc = false;
        foreach (FileInfo file in files)
        {
            try
            {
                (currentContainsDuplicate, currentContainsMisc) = await this.ProcessFile(file.FullName);

                // Assign batch & misc duplicate flag to current if it isn't already set (OR is short-circuiting so this is fast)
                batchContainsDuplicate = batchContainsDuplicate || currentContainsDuplicate;
                batchContainsMisc = batchContainsMisc || currentContainsMisc;
            }
            catch (FormatException f)
            {
                await this.Report($"\t[INVALID FORMAT] {f.Message}\n", ReportLevel.ERROR);
                batchContainsMisc = true;
            }
            catch (Exception ex)
            {
                await this.Report($"\t[SKIP] {ex.Message}\n", ReportLevel.ERROR);
                batchContainsMisc = true;
            }
        }

        return (batchContainsDuplicate, batchContainsMisc);
    }

    /// <summary>
    /// Processes one FP info file.
    /// </summary>
    /// <param name="excelPath">The path to the file to be processed.</param>
    /// <returns>A Task with a duplicate flag and a miscellaneous error flag.</returns>
    /// <exception cref="Exception">When the file does not have a sheet at the specified index.</exception>
    private async Task<(bool, bool)> ProcessFile(string excelPath)
    {
        await this.output.SetCurrentFile(GetFileName(excelPath));

        // Load Excel file, grab the sheet, then close the Excel file
        ISheet sheet;
        using (FileStream fs = new (excelPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        using (IWorkbook workbook = excelPath.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase) ? new XSSFWorkbook(fs) : new HSSFWorkbook(fs))
        {
            sheet = workbook.GetSheetAt(Config.SheetIndex)
                    ?? throw new Exception($"Sheet index {Config.SheetIndex} not found in {GetFileName(excelPath)}.\n");
        }

        // Extract and validate metadata (header row)
        (byte revision, DateTime issueDate, string? issuer) = ParseMetadata(sheet);

        if (issueDate == DateTime.MinValue)
        {
            throw new FormatException($"Could not find a valid issue date in the header area of {GetFileName(excelPath)}.");
        }
        else if (revision == byte.MaxValue)
        {
            throw new FormatException($"Could not find a valid revision number in the header area of {GetFileName(excelPath)}.");
        }
        else if (string.IsNullOrWhiteSpace(issuer))
        {
            throw new FormatException($"Could not find a valid issuer name in the header area of {GetFileName(excelPath)}.");
        }

        // Get column indices associated with column names and verify all necessary columns are present
        Dictionary<string, int> colMap = MapHeaderIndices(sheet);
        foreach (string header in Config.DataHeaderNames)
        {
            if (colMap[header] == -1)
            {
                throw new FormatException($"Missing required column '{header}' in {GetFileName(excelPath)}.");
            }
        }

        // Initialize flags for error detection and intention to repeat
        bool hasDuplicate = false;
        bool hasMiscError = false;
        bool alreadyUploaded = false;
        bool applyAnotherFilter = false;
        bool isNewFile = true;

        // One DB connection to be used across all rows of this file
        using SqlConnection conn = new (Config.GetConnectionString());
        await conn.OpenAsync();

        // Report file start just before the 'apply another filter?' loop to track only new files started
        await this.output.ReportProgress(ProgressEvent.FileStarted);

        // Start the loop for applying multiple filters (run at least once)
        do
        {
            if (!isNewFile)
            {
                await this.output.ReportProgress(ProgressEvent.FileRepeated);
            }

            (string model, bool isFiltering, int targetColIndex) = await this.CollectUserInput(GetFileName(excelPath), isNewFile);
            if (model.Equals("SKIP", StringComparison.OrdinalIgnoreCase))
            {
                await this.output.ReportProgress(ProgressEvent.FileSkipped);
                return (hasDuplicate, hasMiscError);
            }
            else
            {
                isNewFile = true; // For the next iteration
            }

            // Initialize DataTable for rows
            DataTable dt = BuildDataTableFromSheet(sheet, model, revision, issueDate, issuer, colMap, isFiltering, targetColIndex);

            if (dt.Rows.Count > 0)
            {
                try
                {
                    // Attempt a bulk copy
                    await ExecuteBulkCopy(dt, conn);
                }
                catch (Exception)
                {
                    // If bulk copy fails, fall back to row-by-row to find the culprit
                    await this.Report("\t[BULK FAILED] One or more entries in this file could not be added to the database. Switching insertion modes for error reporting...\n", ReportLevel.WARNING);
                    Stack<Report> rowSkipStack = new (); // Use a stack to ensure the skips are printed in the order they appear in the file

                    // Iterate in reverse to guarantee indices don't move on deletion
                    for (int i = dt.Rows.Count - 1; i >= 0; i--)
                    {
                        DataRow dr = dt.Rows[i];
                        try
                        {
                            await WriteRowToDatabase(dr, conn);
                        }
                        catch (SqlException rowEx) when (rowEx.Number == 2627 || rowEx.Number == 2601)
                        {
                            rowSkipStack.Push(new ($"\t[ROW SKIP] Duplicate: Rev {revision}, Location {dr["location"]} Dummy #{dr["dummySampleNum"]}\n", ReportLevel.WARNING));
                            hasDuplicate = true;
                            dt.Rows.RemoveAt(i); // remove the problem row
                        }
                        catch (Exception rowEx)
                        {
                            rowSkipStack.Push(new ($"\t[ROW SKIP] Error: {rowEx.Message}\n", ReportLevel.ERROR));
                            hasMiscError = true;
                            dt.Rows.RemoveAt(i); // remove the problem row
                        }
                    }

                    if (dt.Rows.Count > 0)
                    {
                        while (rowSkipStack.Count > 0)
                        {
                            Report current = rowSkipStack.Pop();
                            await this.Report(current.message, current.level);
                        }
                    }

                    // If every row was duplicate, assume the file was already uploaded for this model.
                    else
                    {
                        await this.Report($"This portion of {GetFileName(excelPath)} has already been uploaded under {model}, so it has been skipped.");
                        alreadyUploaded = true;
                    }
                }
            }

            // Report parse success/failure
            await this.output.ShowPreview(dt);
            this.output.BatchResults.Add(new (excelPath, model, alreadyUploaded, hasDuplicate, hasMiscError, dt.Rows.Count)); // Add a summary row by model and file

            if (isFiltering)
            {
                applyAnotherFilter = await this.input.GetConfirmAsync(new ("\tWould you like to apply another filter/reuse this file for another model?"));
                isNewFile = !applyAnotherFilter;
            }
            else
            {
                applyAnotherFilter = false;
            }
        }
        while (applyAnotherFilter);

        // Files are marked as complete once the user stops collecting data from them
        await this.output.ReportProgress(ProgressEvent.FileCompleted);

        return (hasDuplicate, hasMiscError);
    }

    /// <summary>
    /// Asks the user for C. Core model (mandatory) and column filter (optional), looping until valid input is provided.
    /// </summary>
    /// <param name="filename">The name of the file provided by the user.</param>
    /// <param name="isNewModel">Whether this model is the same as the last one.</param>
    /// <returns>A tuple representing the model, whether there is a filter, and the target column number.</returns>
    private async Task<(string, bool, int)> CollectUserInput(string filename, bool isNewModel)
    {
        string model = string.Empty;
        string? error = null;
        bool isFiltering = false;
        int targetColIndex = -1;

        // This outer loop controls redirects to the model prompt (i.e. bad model name or 'R' in response to the column prompt)
        while (true)
        {
            await this.Report($"{(isNewModel ? "[NEW]" : "[REPEAT]")} {filename}\n", ReportLevel.IMPORTANT);
            Report modelPrompt = new ($"\tPlease enter the C. Core model name for the contents to be imported (or type 'SKIP' to proceed to the next file):");
            model = (await this.input.GetInputAsync(modelPrompt, error)).Trim();

            if (model.Equals("SKIP", StringComparison.OrdinalIgnoreCase))
            {
                await this.Report($"\tSkipping file: {filename}\n", ReportLevel.WARNING);
                return (model, isFiltering, targetColIndex);
            }

            if (!await ValidateModel(model))
            {
                await this.Report($"\t{model} is not in the model to line database. Please try again.\n", ReportLevel.WARNING);
                error = $"{model} is not in the model to line database. Please try again.";
                isNewModel = false;
                continue;
            }

            error = null; // reset error message here to avoid overwriting either prompt

            // This inner loop controls redirects to the column prompt (i.e. bad column )
            while (true)
            {
                string colPrompt = $"\t[{model}] Enter Excel column name BM-CJ ('R' to change model, or ENTER for no filter):";
                string filterColumnName = (await this.input.GetInputAsync(new (colPrompt), error)).Trim();

                if (filterColumnName.Equals("R", StringComparison.OrdinalIgnoreCase))
                { // Signal that this is a repeat, then repeat by breaking the inner loop, redirecting to outer loop
                    isNewModel = false;
                    error = null;
                    break;
                }

                targetColIndex = ColumnIndex(filterColumnName);

                if (string.IsNullOrEmpty(filterColumnName))
                {
                    isFiltering = false;
                    return (model, isFiltering, -1);
                }

                if (targetColIndex >= 64 && targetColIndex <= 87)
                {
                    isFiltering = true;
                    return (model, isFiltering, targetColIndex);
                }

                await this.Report($"\t{filterColumnName} is out of range. Please try again.\n", ReportLevel.WARNING);
                error = $"{filterColumnName} is out of range. Please try again.";
            }
        }
    }

    /// <summary>
    /// Creates a report and passes it to the output provider.
    /// Enclose console-specific information in parentheses for Blazor to hide it.
    /// </summary>
    /// <param name="msg">The message to report.</param>
    /// <param name="level">The message's report level.</param>
    /// <returns>A Task representing that the report has been displayed to the user.</returns>
    private async Task Report(string msg, ReportLevel level = ReportLevel.INFO) => await this.output.ReportAsync(new (msg, level));
}
