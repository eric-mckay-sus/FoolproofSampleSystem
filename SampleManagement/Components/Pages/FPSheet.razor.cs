// <copyright file="FPSheet.razor.cs" company="Stanley Electric US Co. Inc.">
// Copyright (c) 2026 Stanley Electric US Co. Inc. Licensed under the MIT License.
// </copyright>

namespace SampleManagement.Components.Pages;

using System.Data;
using Regex = System.Text.RegularExpressions.Regex;
using FileUploadCommon;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.JSInterop;
using UploadFpInfo;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// Code-behind for the FPSheet page.
/// </summary>
public partial class FPSheet : UploadPageBase<FoolproofEntry>
{
    /// <summary>
    /// The list of files selected for upload.
    /// </summary>
    private readonly IList<IBrowserFile> selectedFiles = [];

    /// <summary>
    /// The list of all models (for autofill on model prompt).
    /// </summary>
    private IList<string> availableModels = [];

    /// <summary>
    /// The current input/confirmation message from <see cref="UploadPageBase{T}.InputProvider"/>.
    /// </summary>
    private string? currentPrompt;

    /// <summary>
    /// The error displayed underneath the input box, if applicable.
    /// </summary>
    private string? inputError;

    /// <summary>
    /// Tracks whether the verbose error log is expanded.
    /// </summary>
    private bool isLogExpanded = false;

    /// <summary>
    /// Debounce for the verbose error log to avoid excessive re-renders.
    /// </summary>
    private CancellationTokenSource? logDebounce;

    /// <summary>
    /// Gets a value indicating whether the current prompt is for model name (or Excel column).
    /// </summary>
    private bool IsModelPrompt => this.currentPrompt != null && this.currentPrompt.Contains("C. Core");

    /// <summary>
    /// When this page is closed, dispose as defined by the parent, then clean up the debounce cancellation token.
    /// </summary>
    public override void Dispose()
    {
        base.Dispose();
        this.logDebounce?.Cancel();
        this.logDebounce?.Dispose();
    }

    /// <summary>
    /// When this page loads, wire the input provider's confirmation and user input events to auto-open an alert (with flag).
    /// Also, set the output's OnNotify event to update the progress bar.
    /// </summary>
    /// <returns>A Task representing that the page is ready.</returns>
    protected override async Task OnInitializedAsync()
    {
        // Link the input provider events to this component's state
        this.InputProvider.OnConfirmationRequested += async (prompt) =>
        {
            this.currentPrompt = prompt.message;
            this.IsAwaitingConfirmation = true;
            await this.InvokeAsync(this.StateHasChanged);
        };

        this.InputProvider.OnInputRequested += async (prompt, error) =>
        {
            this.currentPrompt = ParenthesesClipper().Replace(prompt.message, string.Empty).Trim();
            this.inputError = error;
            this.UserInputText = string.Empty;
            this.IsAwaitingInput = true;
            await this.InvokeAsync(this.StateHasChanged);
            try
            {
                await Task.Delay(100);
                await this.JS.InvokeVoidAsync("focusElement", "model-input");
            }
            catch (OperationCanceledException)
            {
                // This exception is always thrown when a CancellationToken is used
            }
            catch (JSDisconnectedException)
            {
                // This exception is common when dealing with asynchronous JS interop
            }
        };

        // Do the same for the output provider events
        this.Reporter.OnNotify += async () =>
        {
            this.logDebounce?.Cancel();
            this.logDebounce = new ();
            CancellationToken token = this.logDebounce.Token;
            try
            {
                await Task.Delay(100, token);

                await this.InvokeAsync(this.StateHasChanged);

                if (this.Reporter.Logs.Any())
                {
                    // Small delay ensures the DOM has rendered the new <div> before scrolling
                    await Task.Delay(10);
                    await this.JS.InvokeVoidAsync("scrollToBottom", "log-container");
                }
            }
            catch (OperationCanceledException)
            {
                // This exception is always thrown when a CancellationToken is used
            }
            catch (JSDisconnectedException)
            {
                // This exception is common when dealing with asynchronous JS interop
            }
        };

        using (FPSampleDbContext context = this.DbFactory.CreateDbContext())
        {
            this.availableModels = await context.ModelToLine.Select(m => m.ShortDescription).Distinct().ToListAsync();
        }

        this.CurrentSortColumn = "IssueDate";
        this.SortDir = "descending";
        await base.OnInitializedAsync();
    }

    /// <summary>
    /// Executes the actual upload after validation is complete by staging the selected files, then passing their directory to the uploader for a batch (even with just one file).
    /// </summary>
    /// <returns>A Task representing the upload's completion status.</returns>
    protected override async Task<UploadResult> ExecuteUpload()
    {
        // Improbable, but treat like a cancel
        if (!this.selectedFiles.Any())
        {
            return UploadResult.Canceled;
        }

        foreach (IBrowserFile file in this.selectedFiles)
        {
            string trustedFileName = $"{Path.GetFileNameWithoutExtension(file.Name)}_{DateTime.Now:yyyy-MM-dd}{Path.GetExtension(file.Name)}";
            string filePath = Path.Combine(this.UploadsFolderPath, trustedFileName);

            // Stream the file data from the element to the server (must use block using statement to close stream before the uploader tries to create a new one)
            using (FileStream stream = new (filePath, FileMode.Create))
            {
                await file.OpenReadStream().CopyToAsync(stream);
            }
        }

        await this.JS.InvokeVoidAsync("preventConfigurationLoss.setEditorHandler");
        this.Reporter.InitializeProgress(this.selectedFiles.Count);
        FPSheetUploader uploader = new (this.InputProvider, this.Reporter);
        return await uploader.ExecuteAsync(this.UploadsFolderPath); // Batch it even when only one file (for simplicity)
    }

    /// <summary>
    /// <inheritdoc/>
    /// </summary>
    protected override void OnUploadCleanup() => this.selectedFiles.Clear();

    [System.Text.RegularExpressions.GeneratedRegex(@" \(.*?\)")]
    private static partial Regex ParenthesesClipper();

    /// <summary>
    /// Converts the DataTable from the Reporter into a list of DTOs for the UniversalTable.
    /// </summary>
    /// <returns>An IEnumerable of FP rows from the preview DataTable.</returns>
    private IEnumerable<FoolproofPreviewRow> GetPreviewItems()
    {
        if (this.Reporter.CurrentPreview == null)
        {
            return Array.Empty<FoolproofPreviewRow>();
        }

        List<FoolproofPreviewRow>? list = [];
        foreach (DataRow row in this.Reporter.CurrentPreview.Rows)
        {
            list.Add(new FoolproofPreviewRow
            {
                Model = row["model"]?.ToString(),
                FailureMode = row["failureMode"]?.ToString(),
                Location = row["location"]?.ToString(),
                DummySampleNum = row["dummySampleNum"]?.ToString(),
            });
        }

        return list;
    }

    /// <summary>
    /// Gets the summary of the entire batch process from the reporter.
    /// </summary>
    private IEnumerable<FileResult> GetBatchSummary()
    {
        return this.Reporter?.BatchResults ?? Enumerable.Empty<FileResult>();
    }

    /// <summary>
    /// Set the selected file, with guard check to guarantee no visual flicker.
    /// </summary>
    /// <param name="e">The event representing file selection.</param>
    private void HandleFileSelection(InputFileChangeEventArgs e)
    {
        if (this.IsProcessingSelection)
        {
            return;
        }

        this.IsProcessingSelection = true;

        try
        {
            this.selectedFiles.Clear();

            // Default max file count for GetMultipleFiles is 10, which could definitely be encountered. 100, hopefully not.
            foreach (IBrowserFile file in e.GetMultipleFiles(maximumFileCount: 100))
            {
                this.selectedFiles.Add(file);
            }
        }
        finally
        {
            this.IsProcessingSelection = false;
        }
    }

    /// <summary>
    /// Inverts the expansion state of the verbose error log.
    /// </summary>
    private void ToggleLog()
    {
        this.isLogExpanded = !this.isLogExpanded;
    }

    /// <summary>
    /// Unselects all files and exits the upload sequence (to be called from upload confirmation).
    /// </summary>
    private void CancelSelection()
    {
        this.selectedFiles.Clear();
        this.IsUploading = false;
    }

    /// <summary>
    /// Upon receiving confirmation, pass it on to the input provider and throw the flag to close the confirmation alert.
    /// </summary>
    /// <param name="result">Whether to confirm/cancel (t/f).</param>
    private void HandleConfirm(bool result)
    {
        this.IsAwaitingConfirmation = false;
        this.InputProvider.SetConfirmResult(result);
        this.StateHasChanged();
    }

    /// <summary>
    /// Upon receiving user input, pass it on to the input provider, clear the local input, and throw the flag to close the input alert.
    /// </summary>
    private void SubmitUserInput()
    {
        this.IsAwaitingInput = false;
        this.InputProvider.SetInputResult(this.UserInputText);
        this.UserInputText = string.Empty;
        this.StateHasChanged();
    }

    /// <summary>
    /// Helper to be called by the skip button.
    /// </summary>
    /// <param name="newInput">The input to send to the input provider.</param>
    private void SubmitUserInput(string newInput)
    {
        this.UserInputText = newInput;
        this.SubmitUserInput();
    }

    /// <summary>
    /// Finishes the upload by dismissing logs, batch results, and resetting state flags.
    /// </summary>
    private void CloseUpload()
    {
        this.Reporter.ClearLogs();
        this.Reporter.BatchResults.Clear();

        this.Reporter.CurrentPreview = null;
        this.IsUploading = false;
        this.ProgressPercent = 0;
        this.DisplayPercent = 0;

        // Refresh the background data to ensure the main table is up to date
        _ = this.RefreshData();

        this.StateHasChanged();
    }
}
