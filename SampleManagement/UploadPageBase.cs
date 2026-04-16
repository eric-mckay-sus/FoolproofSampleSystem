// <copyright file="UploadPageBase.cs" company="Stanley Electric US Co. Inc.">
// Copyright (c) 2026 Stanley Electric US Co. Inc. Licensed under the MIT License.
// </copyright>

namespace SampleManagement;

using Inject = Microsoft.AspNetCore.Components.InjectAttribute;
using FileUploadCommon;
using ToastType = BlazorBootstrap.ToastType;
using Microsoft.JSInterop;

/// <summary>
/// Represents a generic upload page.
/// </summary>
/// <typeparam name="T"><inheritdoc/></typeparam>
public class UploadPageBase<T> : TableManager<T>, IDisposable
    where T : class
{
    /// <summary>
    /// This session's unique ID (for naming this session's directory).
    /// </summary>
    private static readonly string SessionId = Guid.NewGuid().ToString();

    /// <summary>
    /// Gets or sets the environment in which this upload page runs.
    /// </summary>
    [Inject]
    public IWebHostEnvironment Environment { get; set; } = default!;

    /// <summary>
    /// Gets or sets this upload page's input provider.
    /// </summary>
    [Inject]
    public BlazorInputProvider InputProvider { get; set; } = default!;

    /// <summary>
    /// Gets or sets this upload page's output provider.
    /// </summary>
    [Inject]
    public BlazorReporter Reporter { get; set; } = default!;

    /// <summary>
    /// Gets or sets the JavaScript runtime for reload protection.
    /// </summary>
    [Inject]
    public IJSRuntime JS { get; set; } = default!;

    /// <summary>
    /// Gets the path of the uploads folder for this session.
    /// </summary>
    protected string UploadsFolderPath => Path.Combine(this.Environment.WebRootPath, "uploads", SessionId);

    /// <summary>
    /// Gets or sets a value indicating whether the user is dragging a file over the file input location.
    /// </summary>
    protected bool IsDragging { get; set; } = false;

    /// <summary>
    /// Gets or sets a value indicating whether the user is uploading a file right now.
    /// </summary>
    protected bool IsUploading { get; set; } = false;

    /// <summary>
    /// Gets or sets a value indicating whether <see cref="InputProvider"/> is awaiting confirmation (i.e. whether to display the confirmation dialog).
    /// </summary>
    protected bool IsAwaitingConfirmation { get; set; } = false;

    /// <summary>
    /// Gets or sets a value indicating whether <see cref="InputProvider"/> is awaiting a string of user input (i.e. whether to display the text box dialog).
    /// </summary>
    protected bool IsAwaitingInput { get; set; } = false;

    /// <summary>
    /// Gets or sets a value indicating whether the file manager is currently selecting a file. Guards against file selection flicker.
    /// </summary>
    protected bool IsProcessingSelection { get; set; } = false;

    /// <summary>
    /// Gets or sets the text for the loading bar (from <see cref="Reporter"/> ).
    /// </summary>
    protected string CurrentDisplayStatus { get; set; } = "Idle";

    /// <summary>
    /// Gets or sets the actual progress through the upload.
    /// </summary>
    protected int ProgressPercent { get; set; } = 0;

    /// <summary>
    /// Gets or sets the displayed (elastic) progress through the upload.
    /// </summary>
    protected double DisplayPercent { get; set; } = 0;

    /// <summary>
    /// Gets or sets the timer to determine the frequency of loading updates.
    /// </summary>
    protected System.Timers.Timer? ProgressTimer { get; set; }

    /// <summary>
    /// Gets or sets user input, collected from the search bar.
    /// </summary>
    protected string UserInputText { get; set; } = string.Empty;

    /// <summary>
    /// When this component unloads, unload the timer.
    /// </summary>
    public void Dispose()
    {
        this.ProgressTimer?.Dispose();
        this.CleanupFileSystem();
        this.IsUploading = false;
    }

    /// <summary>
    /// Cleans up this user's files that are on the server (i.e. unfinished uploads).
    /// </summary>
    protected void CleanupFileSystem()
    {
        try
        {
            // Skip directory delete if there were no selected files (thus no need to create a directory)
            if (Directory.Exists(this.UploadsFolderPath))
            {
                // Use recursive mode to delete the directory AND its contents
                Directory.Delete(this.UploadsFolderPath, true);
            }
        }
        catch (IOException ex)
        {
            // Sometimes a file is still "locked" by the OS for a moment after the stream closes. Catch it to avoid a crash.
            this.ToastService.Notify(new (ToastType.Danger, $"Cleanup warning: {ex.Message}"));
        }
    }

    /// <summary>
    /// An 'elastic' progress bar (displayed completion approaches actual completion at higher rate the further they are apart)
    /// Matching the actual upload progress looks too fast, so to give the user good feedback, slow it down artificially.
    /// </summary>
    protected void StartProgressSimulation()
    {
        this.DisplayPercent = 0;
        this.ProgressTimer = new System.Timers.Timer(10); // Update every 10ms
        this.ProgressTimer.Elapsed += (s, e) =>
        {
            // Simple "Ease-Out" logic:
            // Move 10% of the remaining distance to the target each tick
            double diff = this.ProgressPercent - this.DisplayPercent;

            if (diff > 0.1)
            {
                this.DisplayPercent += diff * 0.15; // this factor is the speed parameter
                this.InvokeAsync(this.StateHasChanged);
            }
            else if (this.ProgressPercent > 95 && diff < 5)
            {
                this.DisplayPercent = 100;
                this.ProgressTimer?.Stop();
                this.InvokeAsync(this.StateHasChanged);
            }
        };
        this.ProgressTimer.Start();
    }

    /// <summary>
    /// When a file is dragged into the upload box, throw the drag flag.
    /// </summary>
    protected void HandleDragEnter() => this.IsDragging = true;

    /// <summary>
    /// When a file hovers over the upload box, throw the drag flag.
    /// </summary>
    protected void HandleDragOver() => this.IsDragging = true;

    /// <summary>
    /// When a file is dragged out of into the upload box, reset the drag flag.
    /// </summary>
    protected void HandleDragLeave() => this.IsDragging = false;

    /// <summary>
    /// When a file is placed into the upload box, reset the drag flag.
    /// </summary>
    protected void HandleDrop() => this.IsDragging = false;
}
