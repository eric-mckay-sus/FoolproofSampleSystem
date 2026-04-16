// <copyright file="ModelMappings.razor.cs" company="Stanley Electric US Co. Inc.">
// Copyright (c) 2026 Stanley Electric US Co. Inc. Licensed under the MIT License.
// </copyright>

namespace SampleManagement.Components.Pages;

using FileUploadCommon;
using Microsoft.AspNetCore.Components.Forms;
using UploadModelMappings;
using Microsoft.JSInterop;
using ToastType = BlazorBootstrap.ToastType;

/// <summary>
/// Code-behind for the ModelMappings page.
/// </summary>
public partial class ModelMappings : UploadPageBase<ModelLine>
{
    private IBrowserFile? selectedFile;
    private string? filePath;

    /// <summary>
    /// When this page loads, wire the input provider's confirmation event to auto-open an alert (with flag).
    /// Also, set the output's OnNotify event to update the progress bar.
    /// </summary>
    protected override void OnInitialized()
    {
        // Link the provider events to this component's state
        this.InputProvider.OnConfirmationRequested += (prompt) =>
        {
            this.IsAwaitingConfirmation = true;
            this.InvokeAsync(this.StateHasChanged);
        };

        this.Reporter.OnNotify += () =>
        {
            Report? lastLog = this.Reporter.Logs.LastOrDefault();
            if (lastLog != null)
            {
                // Map CLI strings to GUI Progress
                this.CurrentDisplayStatus = lastLog.message;
                this.ProgressPercent = lastLog.message switch
                {
                    string m when m.Contains("Connecting") => 20,
                    string m when m.Contains("Connected") => 40,
                    string m when m.Contains("Uploading") => 70,
                    string m when m.Contains("Complete") => 101,
                    _ => this.ProgressPercent
                };
            }

            this.InvokeAsync(this.StateHasChanged);
        };

        this.CurrentSortColumn = "ShortDescription";
        this.SortDir = "ascending";
        base.OnInitialized();
    }

    /// <summary>
    /// Set the selected file, with guard check to guarantee no visual flicker.
    /// </summary>
    /// <param name="e">The event representing file selection.</param>
    /// <returns>A Task representing that the file was successfully selected.</returns>
    private async Task HandleFileSelected(InputFileChangeEventArgs e)
    {
        if (this.IsProcessingSelection)
        {
            return;
        }

        this.IsProcessingSelection = true;

        try
        {
            this.selectedFile = e.File;
            await this.StartUpload();
        }
        finally
        {
            this.IsProcessingSelection = false;
        }
    }

    /// <summary>
    /// Upon receiving confirmation, throw the flag to hide the alert and pass the boolean value to the input provider.
    /// In case of cancel, also deselect the file and exit the upload state.
    /// </summary>
    /// <param name="result">Whether to confirm/cancel (t/f).</param>
    private void HandleConfirm(bool result)
    {
        this.IsAwaitingConfirmation = false;
        this.InputProvider.SetConfirmResult(result);
        if (!result)
        {
            this.selectedFile = null;
            this.IsUploading = false;
        }
    }

    /// <summary>
    /// Starts the upload by staging the selected file, then passing its path to the uploader.
    /// </summary>
    /// <returns>A Task representing that the upload is complete.</returns>
    private async Task StartUpload()
    {
        this.ProgressPercent = 5; // Preload to 'feel responsive'
        this.StartProgressSimulation(); // Use fake loading bar
        if (this.selectedFile == null)
        {
            return;
        }

        await Task.Delay(100); // allow dialog activity to finish before beginning

        this.IsUploading = true;
        this.Reporter.ClearLogs();
        try
        {
            // The file content is read into a stream
            if (this.selectedFile != null)
            {
                // Putting the upload in wwwroot works well from a file management standpoint, but will trigger dotnet watch's hot reload. dotnet run works fine.
                Directory.CreateDirectory(this.UploadsFolderPath); // Ensure directory exists

                string trustedFileName = $"model_line_mappings_{DateTime.Now:yyyy-MM-dd}";
                this.filePath = Path.Combine(this.UploadsFolderPath, trustedFileName + Path.GetExtension(this.selectedFile.Name));

                // Stream the file data from the element to the server
                using (FileStream stream = new (this.filePath, FileMode.Create))
                {
                    await this.selectedFile.OpenReadStream().CopyToAsync(stream);
                }

                await this.JS.InvokeVoidAsync("preventConfigurationLoss.setEditorHandler");
                ModelMappingUploader uploader = new (this.InputProvider, this.Reporter);
                await uploader.ExecuteAsync(this.filePath);
                Report? match = this.Reporter.Logs.FirstOrDefault(r => r.level == ReportLevel.ERROR);
                if (match != null)
                {
                    throw new Exception($"{match.message}. Please verify the contents of your file.");
                }

                this.ProgressPercent = 100; // Guarantee the progress bar made it all the way
                await Task.Delay(750); // Make sure it's actually visible to the user for a moment
                await this.RefreshData();
                this.ToastService.Notify(new (ToastType.Success, "Model mappings successfully uploaded!"));
            }
        }
        catch (Exception ex)
        {
            this.ToastService.Notify(new (ToastType.Danger, $"\nUpload failed: {ex.Message}"));
            this.ProgressTimer?.Stop();
        }
        finally
        {
            this.Dispose();
            await this.JS.InvokeVoidAsync("preventConfigurationLoss.clearEditorHandler");
            this.selectedFile = null;
        }
    }
}
