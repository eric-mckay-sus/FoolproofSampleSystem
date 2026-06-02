// <copyright file="TestFakes.cs" company="Stanley Electric US Co. Inc.">
// Copyright (c) 2026 Stanley Electric US Co. Inc. Licensed under the MIT License.
// </copyright>

namespace FoolproofSampleSystem.Tests;

using System.Data;
using InterProcessIO;
using PrintLabel;

/// <summary>
/// Implementation of <see cref="IInputProvider"/> that allows queueing inputs (for testing purposes).
/// </summary>
/// <param name="inputs">The inputs to queue.</param>
/// <param name="confirmation">The confirmation polarity (whether to always confirm or always cancel).</param>
internal sealed class QueueInputProvider(IEnumerable<string>? inputs = null, bool confirmation = true) : IInputProvider
{
    private readonly Queue<string> inputs = new (inputs ?? []);

    /// <summary>
    /// Gets the list of prompts and previous errors that have been queued.
    /// </summary>
    public List<(Report Prompt, string? PreviousError)> InputRequests { get; } = [];

    /// <summary>
    /// Dequeues an input from <see cref="inputs"/> and tracks the input request.
    /// </summary>
    /// <param name="prompt">The prompt to be tracked.</param>
    /// <param name="previousError">The previous error to be tracked, if applicable.</param>
    /// <returns>A string of the received input.</returns>
    public Task<string> GetInputAsync(Report prompt, string? previousError = null)
    {
        this.InputRequests.Add((prompt, previousError));
        return Task.FromResult(this.inputs.Dequeue());
    }

    /// <summary>
    /// Applies the confirmation polarity with which this provider was initialized.
    /// </summary>
    /// <param name="prompt">The prompt awaiting confirmation.</param>
    /// <returns>Whether the prompt was confirmed (or canceled).</returns>
    public Task<bool> GetConfirmAsync(Report prompt) => Task.FromResult(confirmation);
}

/// <summary>
/// Implementation of <see cref="IOutputProvider"/> that captures the output instead of displaying it (for testing).
/// </summary>
internal sealed class CapturingOutputProvider : IOutputProvider
{
    /// <summary>
    /// Gets the name of the current file.
    /// </summary>
    public string? CurrentFileName { get; private set; }

    /// <summary>
    /// Gets or sets the results of the current batch.
    /// </summary>
    public IList<FileResult> BatchResults { get; set; } = [];

    /// <summary>
    /// Gets the list of captured reports.
    /// </summary>
    public List<Report> Reports { get; } = [];

    /// <summary>
    /// Gets the list of captured progress events.
    /// </summary>
    public List<ProgressEvent> ProgressEvents { get; } = [];

    /// <summary>
    /// Gets the list of captured previews.
    /// </summary>
    public List<DataTable> Previews { get; } = [];

    /// <summary>
    /// <inheritdoc/>
    /// </summary>
    /// <param name="name">The new value for <see cref="CurrentFileName"/>.</param>
    /// <returns>A Task representing that <see cref="CurrentFileName"/> has been changed.</returns>
    public Task SetCurrentFile(string name)
    {
        this.CurrentFileName = name;
        return Task.CompletedTask;
    }

    /// <summary>
    /// Adds a <paramref name="report"/> to <see cref="Reports"/>.
    /// </summary>
    /// <param name="report">The <see cref="Report"/> to be added.</param>
    /// <returns>A Task representing that the report list has been updated.</returns>
    public Task ReportAsync(Report report)
    {
        this.Reports.Add(report);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Adds a new progress event to <see cref="ProgressEvents"/>.
    /// </summary>
    /// <param name="ev">The <see cref="ProgressEvent"/> to be added.</param>
    /// <returns>A Task representing that the progress event list has been updated.</returns>
    public Task ReportProgress(ProgressEvent ev)
    {
        this.ProgressEvents.Add(ev);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Adds a new progress event to <see cref="Previews"/>.
    /// </summary>
    /// <param name="dt">The <see cref="DataTable"/> to be added.</param>
    /// <returns>A Task representing that the preview list has been updated.</returns>
    public Task ShowPreview(DataTable dt)
    {
        this.Previews.Add(dt);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Implementation of <see cref="ISampleLabelSource"/> using an in-memory database.
/// </summary>
/// <param name="samples">The samples to be stored in the DB.</param>
internal sealed class InMemorySampleLabelSource(IReadOnlyDictionary<int, string[]> samples) : ISampleLabelSource
{
    /// <summary>
    /// Verifies whether the DB contains a sample with ID = <paramref name="sampleId"/>.
    /// </summary>
    /// <param name="sampleId">The sample ID to check for.</param>
    /// <returns>Whether <paramref name="sampleId"/> has a match in the sample DB.</returns>
    public Task<bool> SampleExistsAsync(int? sampleId) => Task.FromResult(sampleId.HasValue && samples.ContainsKey(sampleId.Value));

    /// <summary>
    /// Gets the fields in the table for the sample with ID = <paramref name="sampleId"/>.
    /// </summary>
    /// <param name="sampleId">The ID of the sample for which to get label fields.</param>
    /// <returns>A string array of the sample fields for the target sample.</returns>
    public Task<string[]> GetLabelFieldsAsync(int? sampleId) => Task.FromResult(sampleId.HasValue && samples.TryGetValue(sampleId.Value, out string[]? fields) ? fields : []);
}

/// <summary>
/// Validaes that a model matches <paramref name="canonicalModel"/>.
/// Effectively, this acts like a normal table validation check when there is only one model in the DB.
/// </summary>
/// <param name="canonicalModel">The model against which to check every other model.</param>
internal sealed class FixedModelValidator(string canonicalModel) : UploadFpInfo.IModelValidator
{
    /// <summary>
    /// Checks if <paramref name="modelName"/> matches the canonical model name in this validator.
    /// </summary>
    /// <param name="modelName">The model to validate.</param>
    /// <returns><inheritdoc/></returns>
    public Task<string?> ValidateAsync(string? modelName) =>
        Task.FromResult(string.Equals(modelName, canonicalModel, StringComparison.OrdinalIgnoreCase) ? canonicalModel : null);
}
