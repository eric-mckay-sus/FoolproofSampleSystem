// <copyright file="ModelMappings.razor.cs" company="Stanley Electric US Co. Inc.">
// Copyright (c) 2026 Stanley Electric US Co. Inc. Licensed under the MIT License.
// </copyright>

namespace SampleManagement.Components.Pages;

using InterProcessIO;
using Microsoft.AspNetCore.Components.Forms;
using UploadModelMappings;

/// <summary>
/// Code-behind for the ModelMappings page.
/// </summary>
public partial class ModelMappings : UploadPageBase<ModelLine>
{
    private IBrowserFile? selectedFile;
    private string? filePath;

    /// <summary>
    /// Gets or sets the text for the loading bar (from <see cref="TableManager{T}.Reporter"/> ).
    /// </summary>
    private string currentDisplayStatus = "Idle";

    /// <summary>
    /// Gets the message to show when <see cref="TableManager{T}.DataView"/> is empty.
    /// </summary>
    public override string EmptyMessage => "No model mappings matching these filters.";

    /// <summary>
    /// When this page loads, wire the input provider's confirmation event to auto-open an alert (with flag).
    /// Also, set the output's OnNotify event to update the progress bar.
    /// </summary>
    /// <returns>A Task representing that the page has loaded.</returns>
    protected override async Task OnInitializedAsync()
    {
        // Link the provider events to this component's state
        this.InputProvider.OnConfirmationRequested += this.HandleConfirmationRequested;

        this.Reporter.OnNotify += this.HandleReporterNotify;
        this.Reporter.OnProgressEventChanged += this.HandleProgressEventChanged;

        this.SortList.Add(new ("Model", SortDir.Asc));
        await base.OnInitializedAsync();
    }

    /// <summary>
    /// When this page is closed, dispose as defined by the parent, then clean up the debounce cancellation token.
    /// </summary>
    /// <param name="disposing">Whether to actually dispose. This is a help for the garbage collector.</param>
    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            this.InputProvider.OnConfirmationRequested -= this.HandleConfirmationRequested;
            this.Reporter.OnNotify -= this.HandleReporterNotify;
        }
    }

    /// <summary>
    /// Applies model/line filters, if applicable.
    /// </summary>
    /// <param name="query"><inheritdoc/></param>
    /// <returns>The query, with model/line filters applied.</returns>
    protected override IQueryable<ModelLine> ApplyFilters(IQueryable<ModelLine> query)
    {
        if (this.ModelFilter.Value != null && this.ModelFilter.IsActive)
        {
            query = query.Where(x => x.Model.Contains(this.ModelFilter.Value));
        }

        if (this.LineFilter.Value != null && this.LineFilter.IsActive)
        {
            query = query.Where(x => x.Line.Contains(this.LineFilter.Value));
        }

        return query;
    }

    /// <summary>
    /// Executes the the actual upload after validation is complete by staging the selected file, then passing its path to the uploader.
    /// </summary>
    /// <returns>A Task representing whether the upload completion status.</returns>
    protected override async Task<UploadResult> ExecuteUpload()
    {
        this.Reporter.InitializeProgress(1);

        // Improbable, but treat like a cancel
        if (this.selectedFile == null)
        {
            return UploadResult.Canceled;
        }

        string extension = Path.GetExtension(this.selectedFile.Name);
        string trustedFileName = $"model_line_mappings_{DateTime.Now:yyyy-MM-dd}";
        this.filePath = Path.Combine(this.UploadsFolderPath, trustedFileName + extension);

        // Stream the file data from the element to the server (must use block using statement to close stream before the uploader tries to create a new one)
        using (FileStream stream = new (this.filePath, FileMode.Create))
        {
            await this.selectedFile.OpenReadStream().CopyToAsync(stream);
        }

        ModelMappingUploader uploader = new (this.InputProvider, this.Reporter);
        return await uploader.ExecuteAsync(this.filePath);
    }

    /// <summary>
    /// <inheritdoc/>
    /// </summary>
    protected override void OnUploadCleanup() => this.selectedFile = null;

    private async Task HandleConfirmationRequested(Report prompt)
    {
        this.IsAwaitingConfirmation = true;
        await this.InvokeAsync(this.StateHasChanged);
    }

    private async Task HandleReporterNotify() => await this.InvokeAsync(this.StateHasChanged);

    /// <summary>
    /// Directly reacts to specific checkpoints during the upload pipeline.
    /// </summary>
    private async Task HandleProgressEventChanged(ProgressEvent ev)
    {
        this.ProgressPercent = ev switch
        {
            ProgressEvent.FileStarted => 10,
            ProgressEvent.ClearStarted => 30,
            ProgressEvent.UploadStarted => 60,
            ProgressEvent.FileCompleted => 95,
            ProgressEvent.UploadComplete => 100,
            _ => this.ProgressPercent
        };

        this.currentDisplayStatus = ev switch
        {
            ProgressEvent.FileStarted => "Initializing parsing stream...",
            ProgressEvent.ClearStarted => "Clearing existing target tables...",
            ProgressEvent.UploadStarted => "Streaming new mappings to table...",
            ProgressEvent.FileCompleted or ProgressEvent.UploadComplete => "Database successfully updated!",
            _ => this.currentDisplayStatus
        };

        await this.InvokeAsync(this.StateHasChanged);
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
            await this.Upload("Model mappings successfully uploaded");
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
}
