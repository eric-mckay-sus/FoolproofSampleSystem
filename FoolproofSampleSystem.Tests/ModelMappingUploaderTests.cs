// <copyright file="ModelMappingUploaderTests.cs" company="Stanley Electric US Co. Inc.">
// Copyright (c) 2026 Stanley Electric US Co. Inc. Licensed under the MIT License.
// </copyright>

namespace FoolproofSampleSystem.Tests;

using InterProcessIO;
using UploadModelMappings;

/// <summary>
/// Tests for the model mapping uploader.
/// </summary>
public sealed class ModelMappingUploaderTests
{
    /// <summary>
    /// Verifies that <see cref="ModelMappingUploader.ParseMappings"/> identifies expected columns.
    /// </summary>
    [Fact]
    public void ParseMappings_MapsExpectedColumns()
    {
        string csvPath = Path.Combine(Path.GetTempPath(), $"model-map-{Guid.NewGuid():N}.csv");
        File.WriteAllText(csvPath, string.Join(
            Environment.NewLine,
            "INTERNAL_PART_#,SHORT_DESC,PROD_CELL_CODE,WORK_CENTER_CODE,DESCRIPTION",
            "ICS-1,Model A,PC01,LINE-1,Full Model A",
            "ICS-2,Model B,PC02,LINE-2,Full Model B"));

        try
        {
            IReadOnlyList<ModelInfo> rows = ModelMappingUploader.ParseMappings(csvPath);

            Assert.Equal(2, rows.Count);
            Assert.Equal("ICS-1", rows[0].IcsNum);
            Assert.Equal("Model A", rows[0].ShortDescription);
            Assert.Equal("PC01", rows[0].ProdCellCode);
            Assert.Equal("LINE-1", rows[0].WorkCenterCode);
            Assert.Equal("Full Model A", rows[0].Description);
        }
        finally
        {
            File.Delete(csvPath);
        }
    }

    /// <summary>
    /// Verifies that the upload is abbreviated before DB interaction when the overwrite confirmation is denied.
    /// </summary>
    /// <returns><inheritdoc/></returns>
    [Fact]
    public async Task ExecuteAsync_ReturnsCanceledBeforeOpeningDatabase_WhenOverwriteIsRejected()
    {
        string csvPath = Path.Combine(Path.GetTempPath(), $"model-map-{Guid.NewGuid():N}.csv");
        await File.WriteAllTextAsync(csvPath, string.Join(
            Environment.NewLine,
            "INTERNAL_PART_#,SHORT_DESC,PROD_CELL_CODE,WORK_CENTER_CODE,DESCRIPTION",
            "ICS-1,Model A,PC01,LINE-1,Full Model A"));
        CapturingOutputProvider output = new ();
        ModelMappingUploader uploader = new (new QueueInputProvider(confirmation: false), output);

        try
        {
            UploadResult result = await uploader.ExecuteAsync(csvPath);

            Assert.Equal(UploadResult.Canceled, result);
            Assert.DoesNotContain(output.ProgressEvents, ev => ev == ProgressEvent.FileStarted || ev == ProgressEvent.UploadStarted);
        }
        finally
        {
            File.Delete(csvPath);
        }
    }

    /// <summary>
    /// Verifies that the uploader rejects non-CSV files.
    /// </summary>
    /// <returns><inheritdoc/></returns>
    [Fact]
    public async Task ExecuteAsync_ReturnsErroredOut_WhenExtensionIsNotCsv()
    {
        string txtPath = Path.Combine(Path.GetTempPath(), $"model-map-{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(txtPath, "not csv");
        CapturingOutputProvider output = new ();
        ModelMappingUploader uploader = new (new QueueInputProvider(), output);

        try
        {
            UploadResult result = await uploader.ExecuteAsync(txtPath);

            Assert.Equal(UploadResult.ErroredOut, result);
            Assert.Contains(output.Reports, report => report.message.Contains("not a CSV", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            File.Delete(txtPath);
        }
    }
}
