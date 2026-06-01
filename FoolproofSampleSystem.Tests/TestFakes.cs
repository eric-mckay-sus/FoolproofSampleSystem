// <copyright file="TestFakes.cs" company="Stanley Electric US Co. Inc.">
// Copyright (c) 2026 Stanley Electric US Co. Inc. Licensed under the MIT License.
// </copyright>

namespace FoolproofSampleSystem.Tests;

using System.Data;
using InterProcessIO;
using PrintLabel;

internal sealed class QueueInputProvider(IEnumerable<string>? inputs = null, bool confirmation = true) : IInputProvider
{
    private readonly Queue<string> inputs = new (inputs ?? []);

    public List<(Report Prompt, string? PreviousError)> InputRequests { get; } = [];

    public Task<string> GetInputAsync(Report prompt, string? previousError = null)
    {
        this.InputRequests.Add((prompt, previousError));
        return Task.FromResult(this.inputs.Dequeue());
    }

    public Task<bool> GetConfirmAsync(Report prompt) => Task.FromResult(confirmation);
}

internal sealed class CapturingOutputProvider : IOutputProvider
{
    public string? CurrentFileName { get; private set; }

    public IList<FileResult> BatchResults { get; set; } = [];

    public List<Report> Reports { get; } = [];

    public List<ProgressEvent> ProgressEvents { get; } = [];

    public List<DataTable> Previews { get; } = [];

    public Task SetCurrentFile(string name)
    {
        this.CurrentFileName = name;
        return Task.CompletedTask;
    }

    public Task ReportAsync(Report report)
    {
        this.Reports.Add(report);
        return Task.CompletedTask;
    }

    public Task ReportProgress(ProgressEvent ev)
    {
        this.ProgressEvents.Add(ev);
        return Task.CompletedTask;
    }

    public Task ShowPreview(DataTable dt)
    {
        this.Previews.Add(dt);
        return Task.CompletedTask;
    }
}

internal sealed class InMemorySampleLabelSource(IReadOnlyDictionary<int, string[]> samples) : ISampleLabelSource
{
    public Task<bool> SampleExistsAsync(int? sampleId) => Task.FromResult(sampleId.HasValue && samples.ContainsKey(sampleId.Value));

    public Task<string[]> GetLabelFieldsAsync(int? sampleId) => Task.FromResult(sampleId.HasValue && samples.TryGetValue(sampleId.Value, out string[]? fields) ? fields : []);
}

internal sealed class FixedModelValidator(string canonicalModel) : UploadFpInfo.IModelValidator
{
    public Task<string?> ValidateAsync(string? modelName) =>
        Task.FromResult<string?>(string.Equals(modelName, canonicalModel, StringComparison.OrdinalIgnoreCase) ? canonicalModel : null);
}
