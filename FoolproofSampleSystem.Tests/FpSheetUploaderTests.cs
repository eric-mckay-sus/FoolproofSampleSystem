// <copyright file="FpSheetUploaderTests.cs" company="Stanley Electric US Co. Inc.">
// Copyright (c) 2026 Stanley Electric US Co. Inc. Licensed under the MIT License.
// </copyright>

namespace FoolproofSampleSystem.Tests;

using System.Data;
using InterProcessIO;
using UploadFpInfo;

public sealed class FpSheetUploaderTests
{
    [Fact]
    public async Task ExecuteAsync_ReturnsErroredOut_WhenPathIsNotAnExcelFile()
    {
        string textPath = Path.Combine(Path.GetTempPath(), $"fp-{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(textPath, "not an excel file");
        CapturingOutputProvider output = new ();
        FPSheetUploader uploader = new (new QueueInputProvider(), output);

        try
        {
            UploadResult result = await uploader.ExecuteAsync(textPath);

            Assert.Equal(UploadResult.ErroredOut, result);
        }
        finally
        {
            File.Delete(textPath);
        }
    }

    [Fact]
    public async Task ProcessFile_BuildsExpectedRowsWithoutOpeningDatabase()
    {
        string workbookPath = FpTestWorkbookBuilder.CreateWorkbook();
        CapturingOutputProvider output = new ();
        DataTable? uploadedTable = null;
        FPSheetUploader uploader = new (
            new QueueInputProvider(["ALPHA", string.Empty]),
            output,
            new FixedModelValidator("ALPHA"),
            dt =>
            {
                uploadedTable = dt.Copy();
                return Task.FromResult(default(ParseResult));
            });

        try
        {
            ParseResult result = await uploader.ProcessFile(workbookPath);

            Assert.False(result.HasDuplicate);
            Assert.NotNull(uploadedTable);
            Assert.Equal(2, uploadedTable!.Rows.Count);
            Assert.Equal("ALPHA", uploadedTable.Rows[0]["model"]);
            Assert.Equal((short)12, uploadedTable.Rows[0]["dummySampleNum"]);
            Assert.Equal((short)34, uploadedTable.Rows[1]["dummySampleNum"]);
            Assert.Contains(output.ProgressEvents, ev => ev == ProgressEvent.FileCompleted);
        }
        finally
        {
            File.Delete(workbookPath);
        }
    }

    [Fact]
    public async Task ProcessFile_AppliesColumnFilterBeforeUpload()
    {
        string workbookPath = FpTestWorkbookBuilder.CreateWorkbook();
        CapturingOutputProvider output = new ();
        DataTable? uploadedTable = null;
        FPSheetUploader uploader = new (
            new QueueInputProvider(["ALPHA", "BM"], confirmation: false),
            output,
            new FixedModelValidator("ALPHA"),
            dt =>
            {
                uploadedTable = dt.Copy();
                return Task.FromResult(default(ParseResult));
            });

        try
        {
            await uploader.ProcessFile(workbookPath);

            Assert.NotNull(uploadedTable);
            Assert.Single(uploadedTable!.Rows);
            Assert.Equal((short)34, uploadedTable.Rows[0]["dummySampleNum"]);
        }
        finally
        {
            File.Delete(workbookPath);
        }
    }
}
