// <copyright file="Program.cs" company="Stanley Electric US Co. Inc.">
// Copyright (c) 2026 Stanley Electric US Co. Inc. Licensed under the MIT License.
// </copyright>

namespace UploadFpInfo;

using System.Data;
using Microsoft.Data.SqlClient;
using NPOI.SS.UserModel; // for generic spreadsheet manipulation
using static Path;

using static NpoiEtlUtilities;
using static DbUploadUtilities;
using InterProcessIO;

/// <summary>
/// Details the high-level parse/upload process for foolproof dummy sample sheets
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
        else if (!Path.Exists(filename))
        {
            await this.Report($"Path '{filename}' is not a valid directory or Excel file. Using Config default ({path}).\n", ReportLevel.WARNING);
        }
        else
        {
            path = filename;
        }

        ParseResult parseResult = default;

        try
        {
            if (Directory.Exists(path))
            {
                parseResult = await this.RunBatch(path);
            }
            else if (File.Exists(path) && IsExcelFile(path))
            {
                parseResult = await this.ProcessFile(path);
            }

            // should never reach here unless the file is somehow deleted during the upload
            else
            {
                await this.Report($"Could not find {path}. Please verify the path is correct, then try again.");
                return UploadResult.ErroredOut;
            }

            // Declare the upload as complete when the batch/file finishes
            await this.output.ReportProgress(ProgressEvent.UploadComplete);

            if (parseResult.HasDuplicate)
            {
                string[] duplicateNames = this.output.BatchResults.Where(fr => fr.parseResult.HasDuplicate).Select(fr => GetFileName(fr.file)).ToArray();
                string report = string.Join("\n\t", duplicateNames);
                await this.Report($"The following files contain duplicate entries:\n\t{report}\nIf you wish to update, do so manually. Otherwise, no action is required.", ReportLevel.WARNING);
            }

            if (parseResult.HasFormatError)
            {
                string[] duplicateNames = this.output.BatchResults.Where(fr => fr.parseResult.HasDuplicate).Select(fr => GetFileName(fr.file)).ToArray();
                string report = string.Join("\n\t", duplicateNames);
                await this.Report($"The following files could not be parsed due to formatting:\n\t{report}\nPlease verify that they are foolproof data sheets and correct the format.", ReportLevel.ERROR);
            }

            if (parseResult.HasMiscError)
            {
                string[] miscNames = this.output.BatchResults.Where(fr => fr.parseResult.HasMiscError).Select(fr => GetFileName(fr.file)).ToArray();
                string report = string.Join("\n\t", miscNames);
                await this.Report($"The following files contain miscellaneous errors:\n{report}\nPlease investigate them to verify why they could not upload.", ReportLevel.ERROR);
            }

            if (parseResult.HasDuplicate || parseResult.HasMiscError)
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
    /// Processes a batch of FP info files.
    /// </summary>
    /// <returns>An tuple representing whether the batch contains a file that 1) contains PK collision(s) and 2) has a miscellaneous error.</returns>
    private async Task<ParseResult> RunBatch(string directoryPath)
    {
        DirectoryInfo inputDir = new (directoryPath);

        FileInfo[] files = inputDir.GetFiles("*.xlsx")
                            .Concat(inputDir.GetFiles("*.xls"))
                            .OrderBy(f => f.Name)
                            .ToArray();

        if (files.Length == 0)
        {
            await this.Report("No Excel files found.", ReportLevel.ERROR);
            return default;
        }

        await this.Report($"Found {files.Length} files. Starting upload to database...\n");

        ParseResult fileResult = default;
        ParseResult batchResult = default;
        foreach (FileInfo file in files)
        {
            try
            {
                fileResult = await this.ProcessFile(file.FullName);

                // Use fileResult as a bitmask to apply new errors to the batch result (see overloaded OR operator in ParseResult)
                batchResult |= fileResult;
            }
            catch (FormatException f)
            {
                await this.Report($"\t[INVALID FORMAT] {f.Message}\n", ReportLevel.ERROR);
                batchResult |= new ParseResult(hasFormatError: true);
            }
            catch (Exception ex)
            {
                await this.Report($"\t[SKIP] {ex.Message}\n", ReportLevel.ERROR);
                batchResult |= new ParseResult(hasMiscError: true);
            }
        }

        return batchResult;
    }

    /// <summary>
    /// Processes one FP info file.
    /// </summary>
    /// <param name="path">The path to the file to be processed.</param>
    /// <returns>A Task containing a <see cref="ParseResult"/> describing this file's success/failure.</returns>
    private async Task<ParseResult> ProcessFile(string path)
    {
        await this.output.SetCurrentFile(GetFileName(path));

        (ISheet sheet, SheetWideData metadata, Dictionary<string, int> colMap) = await LoadAndValidateWorkbook(path);

        // Initialize flags for error detection and intention to repeat
        ParseResult parseResult = default;
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

            (string model, bool isFiltering, int targetColIndex) = await this.CollectUserInput(GetFileName(path), isNewFile);
            if (model.Equals("SKIP", StringComparison.OrdinalIgnoreCase))
            {
                await this.output.ReportProgress(ProgressEvent.FileSkipped);
                return parseResult;
            }
            else
            {
                isNewFile = true; // For the next iteration
            }

            metadata.Model = model;

            // Initialize DataTable for rows
            DataTable dt = BuildDataTableFromSheet(sheet, metadata, colMap, isFiltering, targetColIndex);

            if (dt.Rows.Count > 0)
            {
                parseResult = await this.AttemptUpload(dt, conn);
            }

            // If every row was duplicate, assume the file was already uploaded for this model.
            if (dt.Rows.Count == 0 && parseResult.HasDuplicate)
            {
                await this.Report($"\tThis portion of {GetFileName(path)} has already been uploaded under {metadata.Model}, so it has been skipped.\n", ReportLevel.WARNING);
                parseResult |= new ParseResult { alreadyUploaded = true };
            }

            // Report parse success/failure
            await this.output.ShowPreview(dt);
            this.output.BatchResults.Add(new (path, model, parseResult, dt.Rows.Count)); // Add a summary row by model and file

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

        return parseResult;
    }

    /// <summary>
    /// Attempts to upload all contents of <paramref name="dt"/> over <paramref name="conn"/>.
    /// First attempts a standard SqlBulkCopy for speed, but if that fails, falls back to row-by row for granularity.
    /// </summary>
    /// <param name="dt">The DataTable to upload.</param>
    /// <param name="conn">The SqlConnection used to connect to the database.</param>
    /// <returns>A Task containing the <see cref="ParseResult"/> signifying the success/failure of the upload.</returns>
    private async Task<ParseResult> AttemptUpload(DataTable dt, SqlConnection conn)
    {
        try
        {
            // Attempt a bulk copy
            await ExecuteBulkCopy(dt, conn);
            return default;
        }
        catch (Exception)
        {
            // If bulk copy fails, fall back to row-by-row to find the culprit
            await this.Report("\t[BULK FAILED] One or more entries in this file could not be added to the database. Switching insertion modes for error reporting...\n", ReportLevel.WARNING);
            Stack<Report> rowSkipStack = new (); // Use a stack to ensure the skips are printed in the order they appear in the file
            ParseResult parseResult = default;

            // Iterate in reverse to guarantee indices don't move on deletion
            for (int i = dt.Rows.Count - 1; i >= 0; i--)
            {
                DataRow dr = dt.Rows[i];

                (ParseResult rowResult, Report? skipReport) = await TryWriteRow(dr, conn);
                parseResult |= rowResult;

                if (skipReport != null)
                {
                    rowSkipStack.Push(skipReport);
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
            else
            {
                parseResult |= new ParseResult(alreadyUploaded: true);
            }

            return parseResult;
        }
    }

    /// <summary>
    /// Asks the user for C. Core model (mandatory) and column filter (optional), looping until valid input is provided.
    /// </summary>
    /// <param name="filename">The name of the file provided by the user.</param>
    /// <param name="isNewModel">Whether this model is the same as the last one.</param>
    /// <returns>A tuple representing the model, whether there is a filter, and the target column number.</returns>
    private async Task<(string, bool, int)> CollectUserInput(string filename, bool isNewModel)
    {
        string? potentialModel;
        string? error = null;
        bool isFiltering = false;
        int targetColIndex = -1;

        // Prompt for a model/column filter until satisfied (manual break)
        while (true)
        {
            await this.Report($"{(isNewModel ? "[NEW]" : "[REPEAT]")} {filename}\n", ReportLevel.IMPORTANT);
            Report modelPrompt = new ($"\tPlease enter the C. Core model name for the contents to be imported (or type 'SKIP' to proceed to the next file):");
            potentialModel = (await this.input.GetInputAsync(modelPrompt, error)).Trim();

            // If the user says to skip, return immediately without prompting for any more info
            if (potentialModel.Equals("SKIP", StringComparison.OrdinalIgnoreCase))
            {
                await this.Report($"\tSkipping file: {filename}\n", ReportLevel.WARNING);
                return (potentialModel, isFiltering, targetColIndex);
            }

            string? model = await ValidateModel(potentialModel);

            // Verify that the model actually exists (this is why the MTL database is prerequisite for this program)
            if (string.IsNullOrEmpty(model))
            {
                error = $"'{potentialModel}' is not in the model to line database. Please try again.";
                await this.Report($"\t{error}\n", ReportLevel.WARNING);
                isNewModel = false;
                continue;
            }

            // After the model has been obtained, get an optional column filter
            (bool isFiltering, int targetColIndex)? filterResult = await this.CollectColumnFilter(model);

            // If the returned value is null, that does NOT mean they wish to skip the column filter. Instead, they wish to return to model selection
            if (filterResult is null)
            {
                isNewModel = false;
                error = null;
                continue;
            }

            // Assign the returned filter values so they are actually applied
            isFiltering = filterResult.Value.isFiltering;
            targetColIndex = filterResult.Value.targetColIndex;

            // If we make it here without manually triggering a repeat, the input is valid
            return (model, isFiltering, targetColIndex);
        }
    }

    /// <summary>
    /// Collects the optional column filter from the user.
    /// </summary>
    /// <param name="model">The model for which to collect the column filter.</param>
    /// <returns>A Task holding the updated state of the target column index and whether the filter is desired.</returns>
    private async Task<(bool isFiltering, int targetColIndex)?> CollectColumnFilter(string model)
    {
        string? error = null;
        bool isFiltering = false;
        int targetColIndex = -1;

        // This inner loop controls redirects to the column prompt (i.e. bad column )
        while (true)
        {
            string colPrompt = $"\t[{model}] Enter Excel column name BM-CJ ('R' to change model, or ENTER for no filter):";
            string filterColumnName = (await this.input.GetInputAsync(new (colPrompt), error)).Trim();

            // If the user signals to re-enter model name, return null as a sentinel
            if (filterColumnName.Equals("R", StringComparison.OrdinalIgnoreCase))
            {
                return default;
            }

            // If the user entered some version of nothing, they don't want a column filter
            if (string.IsNullOrEmpty(filterColumnName))
            {
                isFiltering = false;
                return (isFiltering, -1);
            }

            // Otherwise, treat it as a valid column name
            targetColIndex = ColumnIndex(filterColumnName);

            // Make sure it falls in the designated range
            if (targetColIndex >= 64 && targetColIndex <= 87)
            {
                isFiltering = true;
                return (isFiltering, targetColIndex);
            }

            // If it didn't fall in the designated range, notify the user and try again
            await this.Report($"\t{filterColumnName} is out of range. Please try again.\n", ReportLevel.WARNING);
            error = $"{filterColumnName} is out of range. Please try again.";
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
