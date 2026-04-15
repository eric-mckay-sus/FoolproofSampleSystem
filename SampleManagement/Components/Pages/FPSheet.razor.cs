// <copyright file="FPSheet.razor.cs" company="Stanley Electric US Co. Inc.">
// Copyright (c) 2026 Stanley Electric US Co. Inc. Licensed under the MIT License.
// </copyright>

namespace SampleManagement.Components.Pages;

using System.Data;
using FileUploadCommon;
using Microsoft.AspNetCore.Components.Forms;
using UploadFpInfo;
using ToastType = BlazorBootstrap.ToastType;

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
    /// The current input/confirmation prompt from <see cref="UploadPageBase{T}.InputProvider"/>.
    /// </summary>
    private Report? currentPrompt;

    /// <summary>
    /// Gets the amount of progress allotted to one checkpoint.
    /// </summary>
    private int ProgIncrement => 96 / (2 * this.selectedFiles.Count);

    /// <summary>
    /// When this page loads, wire the input provider's confirmation and user input events to auto-open an alert (with flag).
    /// Also, set the output's OnNotify event to update the progress bar.
    /// </summary>
    protected override void OnInitialized()
    {
        // Link the input provider events to this component's state
        this.InputProvider.OnConfirmationRequested += (prompt) =>
        {
            this.currentPrompt = prompt;
            this.IsAwaitingConfirmation = true;
            this.InvokeAsync(this.StateHasChanged);
        };

        this.InputProvider.OnInputRequested += (prompt) =>
        {
            this.currentPrompt = prompt;
            this.UserInputText = string.Empty;
            this.IsAwaitingInput = true;
            this.InvokeAsync(this.StateHasChanged);
        };

        // Do the same for the output provider event
        this.Reporter.OnNotify += () =>
        {
            Report? lastLog = this.Reporter.Logs.LastOrDefault();
            if (lastLog != null)
            {
                // Map CLI strings to GUI Progress
                this.CurrentDisplayStatus = lastLog.message;
                if (lastLog.message.Contains("NEW") || lastLog.message.Contains("UPLOAD") || lastLog.message.Contains("Skipping"))
                {
                    // Clamp the progress so it never exceeds 100
                    this.ProgressPercent = Math.Min(100, this.ProgressPercent + this.ProgIncrement);
                }
            }

            this.InvokeAsync(this.StateHasChanged);
        };
    }

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
    /// Starts the upload by staging the selected files, then passing their directory to the uploader for a batch (even with just one file).
    /// </summary>
    /// <returns>A Task representing that the upload is complete.</returns>
    private async Task StartUpload()
    {
        this.ProgressPercent = 5; // Preload to 'feel responsive'
        this.StartProgressSimulation(); // Use fake loading bar
        if (!this.selectedFiles.Any())
        {
            return;
        }

        await Task.Delay(100); // allow dialog activity to finish before beginning

        this.IsUploading = true;
        this.Reporter.ClearLogs();
        string uploadsFolderPath = Path.Combine(this.Environment.WebRootPath, "uploads");
        try
        {
            // The file content is read into a stream
            if (this.selectedFiles.Any())
            {
                // Putting the upload in wwwroot works well from a file management standpoint, but will trigger dotnet watch's hot reload. dotnet run works fine.
                Directory.CreateDirectory(uploadsFolderPath); // Ensure directory exists

                foreach (IBrowserFile file in this.selectedFiles)
                {
                    string trustedFileName = $"{Path.GetFileNameWithoutExtension(file.Name)}_{DateTime.Now:yyyy-MM-dd}{Path.GetExtension(file.Name)}";
                    string filePath = Path.Combine(uploadsFolderPath, trustedFileName);

                    // Stream the file data from the element to the server
                    using (FileStream stream = new (filePath, FileMode.Create))
                    {
                        await file.OpenReadStream().CopyToAsync(stream);
                    }
                }

                FPSheetUploader uploader = new (this.InputProvider, this.Reporter);
                await uploader.ExecuteAsync(uploadsFolderPath); // Batch it even when only one file (for simplicity)
                Report? match = this.Reporter.Logs.FirstOrDefault(r => r.level == ReportLevel.ERROR);
                if (match != null)
                {
                    throw new Exception($"{match.message}. Please verify the contents of your file.");
                }

                this.ProgressPercent = 100; // Guarantee the progress bar made it all the way
                await Task.Delay(750); // Make sure it's actually visible to the user for a moment
                await this.RefreshData();
                this.Reporter.ClearLogs();
                this.ToastService.Notify(new (ToastType.Success, "Foolproof sheets successfully uploaded!"));
            }
        }
        catch (Exception ex)
        {
            this.ToastService.Notify(new (ToastType.Danger, $"\nUpload failed: {ex.Message}"));
        }
        finally
        {
            try
            {
                // Skip directory delete if there were no selected files (thus no need to create a directory)
                if (Directory.Exists(uploadsFolderPath))
                {
                    // Use recursive mode to delete the directory AND its contents
                    Directory.Delete(uploadsFolderPath, true);
                }
            }
            catch (IOException ex)
            {
                // Sometimes a file is still "locked" by the OS for a moment after the stream closes. Catch it to avoid a crash.
                this.ToastService.Notify(new (ToastType.Danger, $"Cleanup warning: {ex.Message}"));
            }

            this.selectedFiles.Clear();
            this.IsUploading = false;
            this.ProgressTimer?.Dispose();
        }
    }
}
