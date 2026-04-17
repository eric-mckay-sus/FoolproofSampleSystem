// <copyright file="FluidIO.cs" company="Stanley Electric US Co. Inc.">
// Copyright (c) 2026 Stanley Electric US Co. Inc. Licensed under the MIT License.
// </copyright>
namespace FileUploadCommon;

using System.Data;
using StringBuilder = System.Text.StringBuilder;

/// <summary>
/// Represents a report's metadata.
/// </summary>
public enum ReportLevel
{
    /// <summary>
    /// The default report
    /// </summary>
    INFO,

    /// <summary>
    /// A report with higher importance than INFO, but no special meaning
    /// </summary>
    IMPORTANT,

    /// <summary>
    /// A report representing successful completion
    /// </summary>
    SUCCESS,

    /// <summary>
    /// A report denoting something has failed, but will try again/fall back on a default
    /// </summary>
    WARNING,

    /// <summary>
    /// A report denoting something has failed (without retry)
    /// </summary>
    ERROR,
}

/// <summary>
/// Defines a 'checkpoint' event to be watched by any output source that wishes to track progress.
/// </summary>
public enum ProgressEvent
{
    /// <summary>
    /// The event representing when a new file has started.
    /// </summary>
    FileStarted,

    /// <summary>
    /// The event representing when a file has been skipped.
    /// </summary>
    FileSkipped,

    /// <summary>
    /// The event representing when a file has been completeted (after 'repeat this file?' has been denied).
    /// </summary>
    FileCompleted,

    /// <summary>
    /// The event representing when the entire upload is complete.
    /// </summary>
    UploadComplete,
}

/// <summary>
/// Denotes the status with which an upload finished.
/// </summary>
public enum UploadResult
{
    /// <summary>
    /// The upload finished without any errors.
    /// </summary>
    Complete,

    /// <summary>
    /// The upload ran to completion with errors.
    /// </summary>
    CompleteWithErrors,

    /// <summary>
    /// The upload was canceled by the process because of a fatal error.
    /// </summary>
    ErroredOut,

    /// <summary>
    /// The upload was canceled by the user.
    /// </summary>
    Canceled,
}

/// <summary>
/// The data packet used by I/O classes to track a file with its upload status
/// </summary>
/// <param name="file">The file containing the model.</param>
/// <param name="model">The model name (from C. Core).</param>
/// <param name="hadDuplicates">Whether the model upload encountered duplicates.</param>
/// <param name="hadErrors">Whether the model upload encountered other errors.</param>
/// <param name="rowsUploaded">The number of rows uploaded for this model.</param>
public record FileResult(string file, string model, bool hadDuplicates, bool hadErrors, int rowsUploaded);

/// <summary>
/// The data packet used by I/O classes to associate a report level with the report message.
/// Different input/output providers can format differently based on the report level.
/// </summary>
/// <param name="message">The base message</param>
/// <param name="level">The message's metadata as a report level</param>
public record Report(string message, ReportLevel level = ReportLevel.INFO);

/// <summary>
/// Defines thread-safe asynchronous input redirection.
/// </summary>
public interface IInputProvider
{
    /// <summary>
    /// Prompts and awaits user input.
    /// </summary>
    /// <param name="prompt">The prompt requiring user input.</param>
    /// <param name="previousError">The previous error that prompted this input, if applicable.</param>
    /// <returns>A Task containing the user's input.</returns>
    Task<string> GetInputAsync(Report prompt, string? previousError = null);

    /// <summary>
    /// Prompts and awaits user input for a yes/no question.
    /// Like <see cref="GetInputAsync"/>, but returns a boolean instead of a string.
    /// Recommend calling <see cref="GetInputAsync"/> internally.
    /// </summary>
    /// <param name="prompt">The prompt requiring confirmation.</param>
    /// <returns>A Task containing a boolean representing whether the prompt was confirmed.</returns>
    Task<bool> GetConfirmAsync(Report prompt);
}

/// <summary>
/// Defines thread-safe asynchronous output redirection for Report records (custom IProgress).
/// </summary>
public interface IOutputProvider
{
    /// <summary>
    /// Gets the name of the file currently being processed.
    /// </summary>
    public string? CurrentFileName { get; }

    /// <summary>
    /// Gets or sets the results of this batch.
    /// </summary>
    public IList<FileResult> BatchResults { get; set; }

    /// <summary>
    /// Sets <see cref="CurrentFileName"/> to <paramref name="name"/>.
    /// Distinct from the <see cref="CurrentFileName"/> setter property to allow only setting it privately.
    /// </summary>
    /// <param name="name">The new current file name.</param>
    /// <returns>A Task representing that the file name has been changed.</returns>
    Task SetCurrentFile(string name);

    /// <summary>
    /// Asynchronously displays a report to the output.
    /// </summary>
    /// <param name="report">The <see cref="Report"/> record to be displayed.</param>
    /// <returns>A task signaling that the output has finished reporting.</returns>
    Task ReportAsync(Report report);

    /// <summary>
    /// Asynchronously reports progress to the output.
    /// </summary>
    /// <param name="ev">The <see cref="ProgressEvent"/> to report.</param>
    /// <returns>A Task representing that the output has received the progress report.</returns>
    Task ReportProgress(ProgressEvent ev);

    /// <summary>
    /// Asynchronously displays <paramref name="dt"/> to the output.
    /// </summary>
    /// <param name="dt">The DataTable to display.</param>
    /// <returns>A Task representing the completion of the method.</returns>
    Task ShowPreview(DataTable dt);
}

/// <summary>
/// An implementation of IInputProvider to get input from the console (works kind of like Python's native input() method).
/// </summary>
public class ConsoleInputProvider : IInputProvider
{
    /// <summary>
    /// <inheritdoc/> Uses standard console methods suitable for a CLI.
    /// </summary>
    /// <param name="prompt"><inheritdoc/></param>
    /// <param name="previousError">Unused in this implementation.</param>
    /// <returns>A Task containing the command line input.</returns>
    public async Task<string> GetInputAsync(Report prompt, string? previousError = null)
    {
        Console.WriteLine(prompt.ToAnsiString());
        Console.Write('\t');
        return await Task.Run(() => Console.ReadLine() ?? string.Empty);
    }

    /// <summary>
    /// <inheritdoc/>
    /// Uses standard console methods suitable for a CLI.
    /// Always appends (y/n) and checks for 'yes' or 'y'.
    /// </summary>
    /// <param name="prompt"><inheritdoc/></param>
    /// <returns>A Task-wrapped boolean representing the whether the user confirmed.</returns>
    public async Task<bool> GetConfirmAsync(Report prompt)
    {
        Console.Write($"{prompt.ToAnsiString()} (y/n)");
        string response = (await this.GetInputAsync(new (string.Empty))).Trim().ToLower();
        return response == "y" || response == "yes";
    }
}

/// <summary>
/// An implementation of IReportOutputProvider that prints to the console.
/// </summary>
public class ConsoleReporter : IOutputProvider
{
    /// <summary>
    /// Gets the name of the file currently being processed.
    /// </summary>
    public string CurrentFileName { get; private set; } = string.Empty;

    /// <summary>
    /// Gets or sets the results for each file in this batch.
    /// </summary>
    public IList<FileResult> BatchResults { get; set; } = [];

    /// <summary>
    /// Sets <see cref="CurrentFileName"/> to <paramref name="name"/>.
    /// </summary>
    /// <param name="name">The name to assign to <see cref="CurrentFileName"/>.</param>
    /// <returns><inheritdoc/></returns>
    public async Task SetCurrentFile(string name)
    {
        this.CurrentFileName = name;
        await Task.CompletedTask;
    }

    /// <summary>
    /// <inheritdoc/>
    /// In this case, the output is the console, so we just use Console.Write.
    /// </summary>
    /// <param name="report"><inheritdoc/></param>
    /// <returns>A Task representing that the console has finished printing.</returns>
    public async Task ReportAsync(Report report)
    {
        Console.Write(report.ToAnsiString());
        await Task.CompletedTask;
    }

    /// <summary>
    /// Asynchronously reports progress to the console.
    /// At the moment, this is not used, it simply implements the interface.
    /// </summary>
    /// <param name="ev"><inheritdoc/></param>
    /// <returns>A Task representing that the console has reported progress.</returns>
    public async Task ReportProgress(ProgressEvent ev) => await Task.CompletedTask;

    /// <summary>
    /// Prints the contents of <paramref name="dt"/> to the console.
    /// </summary>
    /// <param name="dt"><inheritdoc/></param>
    /// <returns>A Task representing that the preview has been shown.</returns>
    public async Task ShowPreview(DataTable dt)
    {
        // If there's no content to print, tell the user and exit
        if (dt.Rows.Count == 0)
        {
            await this.ReportAsync(new ("\tNo rows with valid data (under current filters).\n", ReportLevel.WARNING));
            return;
        }

        StringBuilder sb = new ();

        sb.Append(new Report($"\t--- UPLOAD SUMMARY: {dt.Rows.Count} ROWS PROCESSED ---\n\t", ReportLevel.SUCCESS).ToAnsiString());

        // Define column widths for the ASCII table
        int modelWidth = 15;
        int modeWidth = 45;
        int locWidth = 15;
        int dummyWidth = 5;

        // Print table header
        string header = $"| {"Model".PadRight(modelWidth)} | {"Failure Mode".PadRight(modeWidth)} | {"Loc".PadRight(locWidth)} | {"Dummy #".PadRight(dummyWidth)} |\n\t";
        string divider = $"{new ('-', header.Length)}\n\t";

        sb.Append(divider);
        sb.Append(header);
        sb.Append(divider);

        // Print each row from the DataTable
        foreach (DataRow row in dt.Rows)
        {
            string modelStr = row["model"]?.ToString()?.Length > modelWidth
                ? string.Concat(row["model"].ToString().AsSpan(0, modelWidth - 3), "...")
                : row["model"]?.ToString() ?? string.Empty;

            string modeStr = row["failureMode"]?.ToString()?.Length > modeWidth
                ? string.Concat(row["failureMode"].ToString().AsSpan(0, modeWidth - 3), "...")
                : row["failureMode"]?.ToString() ?? string.Empty;

            string locStr = row["location"]?.ToString()?.Length > locWidth
                ? string.Concat(row["location"].ToString().AsSpan(0, locWidth - 3), "...")
                : row["location"]?.ToString() ?? string.Empty;

            string dummyStr = row["dummySampleNum"]?.ToString() ?? string.Empty;

            string line = $"| {modelStr.PadRight(modelWidth)} | {modeStr.PadRight(modeWidth)} | {locStr.PadRight(locWidth)} | {dummyStr.PadRight(dummyWidth)} |\n\t";
            sb.Append(new Report(line).ToAnsiString());
        }

        sb.Append(divider.TrimEnd('\t'));

        await this.ReportAsync(new (sb.ToString()));
    }
}

/// <summary>
/// An implementation of IInputProvider to get input from Blazor using a TaskCompletionSource.
/// Register this as a service within SampleManagement/Program.cs.
/// </summary>
public class BlazorInputProvider : IInputProvider
{
    /// <summary>
    /// Controls the completion state of an input request.
    /// Blazor may control this as it sees fit without using a blocking call (thereby freezing itself bc Blazor is single-thread).
    /// </summary>
    private TaskCompletionSource<string>? inputTcs;

    /// <summary>
    /// Controls the completion state of a confirmation request.
    /// Blazor may control this as it sees fit without using a blocking call (thereby freezing itself bc Blazor is single-thread).
    /// </summary>
    private TaskCompletionSource<bool>? confirmTcs;

    /// <summary>
    /// The Blazor action to perform when GetInputAsync is called.
    /// </summary>
    public event Action<Report, string?>? OnInputRequested;

    /// <summary>
    /// The Blazor action to perform when a simple yes/no confirmation is requested
    /// </summary>
    public event Action<Report>? OnConfirmationRequested;

    /// <summary>
    /// <inheritdoc/>
    /// </summary>
    /// <param name="prompt">The prompt to show the user.</param>
    /// <param name="previousError">The error prompting this input, if applicable.</param>
    /// <returns>A Task containing the string collected from Blazor.</returns>
    public Task<string> GetInputAsync(Report prompt, string? previousError = null)
    {
        this.inputTcs = new TaskCompletionSource<string>();
        this.OnInputRequested?.Invoke(prompt, previousError);
        return this.inputTcs.Task;
    }

    /// <summary>
    /// <inheritdoc/>
    /// </summary>
    /// <param name="prompt">The prompt to show the user.</param>
    /// <returns>A Task containing the confirmation (or cancellation) from Blazor.</returns>
    public Task<bool> GetConfirmAsync(Report prompt)
    {
        this.confirmTcs = new TaskCompletionSource<bool>();
        this.OnConfirmationRequested?.Invoke(prompt);
        return this.confirmTcs.Task;
    }

    /// <summary>
    /// Fills <see cref="inputTcs"/> with <paramref name="result"/>.
    /// </summary>
    /// <param name="result">The desired contents of <see cref="inputTcs"/>. </param>
    public void SetInputResult(string result) => this.inputTcs?.TrySetResult(result);

    /// <summary>
    /// Fills <see cref="confirmTcs"/> with <paramref name="result"/>.
    /// </summary>
    /// <param name="result">The desired contents of <see cref="confirmTcs"/>. </param>
    public void SetConfirmResult(bool result) => this.confirmTcs?.TrySetResult(result);
}

/// <summary>
/// An implementation of <see cref="IOutputProvider"/> that informs a Blazor page to show the new data.
/// </summary>
public class BlazorReporter : IOutputProvider
{
    /// <summary>
    /// Notify the UI to re-render. The Blazor page must bind its StateHasChanged method to this Action.
    /// </summary>
    public event Action? OnNotify;

    /// <summary>
    /// Notify the UI of a new progress update. The Blazor page must subscribe to this event to update the loading bar.
    /// </summary>
    public event Action<ProgressEvent>? OnProgress;

    /// <summary>
    /// Gets the name of the file currently being processed.
    /// </summary>
    public string CurrentFileName { get; private set; } = string.Empty;

    /// <summary>
    /// Gets or sets the results for each file in this batch.
    /// </summary>
    public IList<FileResult> BatchResults { get; set; } = [];

    /// <summary>
    /// Gets the list of <see cref="Report"/> objects that document a warning or error.
    /// </summary>
    public IList<Report> Logs { get; private set; } = [];

    /// <summary>
    /// Gets the underlying DataTable object that stores the preview information.
    /// It is important that Blazor persists this so it has concrete data during another upload.
    /// </summary>
    public DataTable? CurrentPreview { get; private set; }

    /// <summary>
    /// Sets <see cref="CurrentFileName"/> to <paramref name="name"/>, then notifies Blazor.
    /// </summary>
    /// <param name="name">The name to assign to <see cref="CurrentFileName"/>.</param>
    /// <returns><inheritdoc/></returns>
    public Task SetCurrentFile(string name)
    {
        this.CurrentFileName = name;
        this.OnNotify?.Invoke();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Clears the logs so old data does not persist.
    /// </summary>
    public void ClearLogs()
    {
        this.Logs = [];
        this.CurrentPreview = null;
        this.OnNotify?.Invoke();
    }

    /// <summary>
    /// <inheritdoc/>
    /// </summary>
    /// <param name="report">The <see cref="Report"/> object to pass to Blazor.</param>
    /// <returns>A Task denoting that Blazor has received and displayed the message.</returns>
    public async Task ReportAsync(Report report)
    {
        // Only log errors to the Blazor interface
        if ((report.level == ReportLevel.WARNING || report.level == ReportLevel.ERROR) && !report.message.Contains("Please try again"))
        {
            this.Logs.Add(report);
        }

        this.OnNotify?.Invoke();
        await Task.CompletedTask;
    }

    /// <summary>
    /// Reports a progress update by firing the <see cref="OnProgress"/> event, to which the Blazor page has subscribed.
    /// </summary>
    /// <param name="ev"><inheritdoc/></param>
    /// <returns>A Task representing that the Blazor page has received the message.</returns>
    public Task ReportProgress(ProgressEvent ev)
    {
        this.OnProgress?.Invoke(ev);
        return Task.CompletedTask;
    }

    /// <summary>
    /// <inheritdoc/>
    /// </summary>
    /// <param name="dt">The DataTable to display.</param>
    /// <returns>A Task denoting that Blazor has displayed the preview.</returns>
    public async Task ShowPreview(DataTable dt)
    {
        this.CurrentPreview = dt;
        this.OnNotify?.Invoke();
        await Task.CompletedTask;
    }
}
