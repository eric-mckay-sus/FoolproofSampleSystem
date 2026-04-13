// <copyright file="FluidIO.cs" company="Stanley Electric US Co. Inc.">
// Copyright (c) 2026 Stanley Electric US Co. Inc. Licensed under the MIT License.
// </copyright>
namespace UploadFpInfo;

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
    /// A report denoting something has failed, but will try again/fall back on a default
    /// </summary>
    WARNING,

    /// <summary>
    /// A report denoting something has failed (without retry)
    /// </summary>
    ERROR,

    /// <summary>
    /// A report representing successful completion
    /// </summary>
    SUCCESS,
}

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
    /// <returns>A Task containing the user's input.</returns>
    Task<string> GetInputAsync(Report prompt);

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
public interface IReportOutputProvider
{
    /// <summary>
    /// Asynchronously displays a report to the output.
    /// </summary>
    /// <param name="report">The report record to be displayed.</param>
    /// <returns>A task signaling that the output has finished reporting.</returns>
    Task ReportAsync(Report report);

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
    /// <returns>A Task containing the command line input.</returns>
    public async Task<string> GetInputAsync(Report prompt)
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
public class ConsoleReporter : IReportOutputProvider
{
    /// <summary>
    /// <inheritdoc/>
    /// In this case, the output is the console, so we just use Console.Write.
    /// </summary>
    /// <param name="report"><inheritdoc/></param>
    /// <returns>A Task representing that the console has finished printing.</returns>
    public Task ReportAsync(Report report)
    {
        Console.Write(report.ToAnsiString());
        return Task.CompletedTask;
    }

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

// Vague idea for how to do reporting in Blazor
// public class BlazorReporter : IReportOutputProvider
// {
//     private readonly MyComponent _owner;
//     public BlazorReporter(MyComponent owner) => _owner = owner;

// public async Task ReportAsync(Report report)
//     {
//         _owner.Messages.Add(report);
//         await _owner.InvokeAsync(_owner.StateHasChanged);
//     }
// }

// Blazor input handler will require a TaskCompletionSource to allow the
// GetInputAsync call to block the uploader logic without freezing the server/browser.
// Bind the submit button to SetResult on the TaskCompletionSource to resume the uploader
