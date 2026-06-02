// <copyright file="FpInfoEtlUtilitiesTests.cs" company="Stanley Electric US Co. Inc.">
// Copyright (c) 2026 Stanley Electric US Co. Inc. Licensed under the MIT License.
// </copyright>

namespace FoolproofSampleSystem.Tests;

using System.Data;
using System.IO;
using InterProcessIO;
using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;
using UploadFpInfo;

/// <summary>
/// Tests for the ETL utilities class for the FP data sheet uploader.
/// </summary>
public sealed class FpInfoEtlUtilitiesTests
{
    /// <summary>
    /// Verifies that <see cref="NpoiEtlUtilities.ExtractPartNumber"/> actually extracts the part number.
    /// </summary>
    /// <param name="raw">The string from which to extract a part number.</param>
    /// <param name="expected">The expected 16-bit number retrieved by the method.</param>
    [Theory]
    [InlineData("#12 required", (short)12)]
    [InlineData("dummy sample 34", (short)34)]
    [InlineData("none", null)]
    [InlineData("", null)]
    public void ExtractPartNumber_ReturnsExpectedNumber(string raw, short? expected)
    {
        Assert.Equal(expected, NpoiEtlUtilities.ExtractPartNumber(raw));
    }

    /// <summary>
    /// Verifies that <see cref="NpoiEtlUtilities.TranslateRevString"/> actually translates the rev string.
    /// </summary>
    /// <param name="raw">The rev string as it might appear in the FP sheet.</param>
    /// <param name="expected">The expected integer retrieved by the method.</param>
    [Theory]
    [InlineData("ORIG", 0)]
    [InlineData("Draft", 0)]
    [InlineData("REV7", 7)]
    [InlineData("R12", 12)]
    [InlineData("not a rev", byte.MaxValue)]
    public void TranslateRevString_HandlesAliasesAndNumericRevisions(string raw, byte expected)
    {
        Assert.Equal(expected, NpoiEtlUtilities.TranslateRevString(raw));
    }

    /// <summary>
    /// Verifies that a fresh FP DataTable has all type and length constraints in place.
    /// </summary>
    [Fact]
    public void CreateFoolproofDataTable_EnforcesColumnTypesAndLengthsInMemory()
    {
        DataTable table = NpoiEtlUtilities.CreateFoolproofDataTable();

        Assert.Equal(typeof(byte), table.Columns["revision"]!.DataType);
        Assert.Equal(typeof(short), table.Columns["dummySampleNum"]!.DataType);
        Assert.Equal(32, table.Columns["model"]!.MaxLength);
        Assert.Equal(1, table.Columns["rank"]!.MaxLength);
    }

    /// <summary>
    /// Verifies that the column index identifier correctly translates multi-letter Excel columns to their 0-based integer index.
    /// </summary>
    /// <param name="column">The string column name.</param>
    /// <param name="expected">The integer translation.</param>
    [Theory]
    [InlineData("A", 0)]
    [InlineData("Z", 25)]
    [InlineData("AA", 26)]
    [InlineData("BM", 64)]
    [InlineData("CJ", 87)]
    [InlineData("", -1)]
    public void ColumnIndex_ConvertsExcelLetters(string column, int expected)
    {
        Assert.Equal(expected, NpoiEtlUtilities.ColumnIndex(column));
    }

    /// <summary>
    /// Verifies that the accepted extension method allows both .xls and .xlsx files, and denies others.
    /// </summary>
    /// <param name="path">The dummy file path.</param>
    /// <param name="expected">The verdict on whether it is an Excel file.</param>
    [Theory]
    [InlineData("sheet.xlsx", true)]
    [InlineData("sheet.xls", true)]
    [InlineData("sheet.csv", false)]
    public void IsExcelFile_DetectsSupportedExtensions(string path, bool expected)
    {
        Assert.Equal(expected, NpoiEtlUtilities.IsExcelFile(path));
    }

    /// <summary>
    /// Verifies that the column filter is restricted to BM-CJ.
    /// </summary>
    /// <param name="column">The column name to test.</param>
    /// <param name="expectedIndex">The index that should be returned by the method.</param>
    /// <param name="expectedValid">The verdict on whether the column is allowed to be a filter.</param>
    [Theory]
    [InlineData("BM", 64, true)]
    [InlineData("CJ", 87, true)]
    [InlineData("BL", 63, false)]
    [InlineData("CK", 88, false)]
    public void TryParseFilterColumn_EnforcesBmThroughCjRange(string column, int expectedIndex, bool expectedValid)
    {
        bool isValid = NpoiEtlUtilities.TryParseFilterColumn(column, out int targetColIndex);

        Assert.Equal(expectedValid, isValid);
        Assert.Equal(expectedIndex, targetColIndex);
    }

    /// <summary>
    /// Verifies that <see cref="NpoiEtlUtilities.ParseMetadata"/> internally standardizes date and revision formatting.
    /// </summary>
    /// <returns><inheritdoc/></returns>
    [Fact]
    public async Task ParseMetadata_CleansDateSuffixesAndResolvesRevisionAliases()
    {
        string workbookPath = FpTestWorkbookBuilder.CreateWorkbook(revision: "Draft", issueDate: "15th January 2026", issuer: "QA Lead");

        try
        {
            (_, SheetWideData metadata, _) = await NpoiEtlUtilities.LoadAndValidateWorkbook(workbookPath);

            Assert.Equal((byte)0, metadata.Revision);
            Assert.Equal(new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Unspecified), metadata.IssueDate);
            Assert.Equal("QA Lead", metadata.Issuer);
            Assert.Null(metadata.Model);
        }
        finally
        {
            File.Delete(workbookPath);
        }
    }

    /// <summary>
    /// Verifies that <see cref="NpoiEtlUtilities.MapHeaderIndices"/> is case insensitive and marks missing headers.
    /// </summary>
    [Fact]
    public void MapHeaderIndices_IsCaseInsensitiveAndMarksMissingHeaders()
    {
        XSSFWorkbook workbook = new ();
        ISheet sheet = workbook.CreateSheet("Sheet1");
        IRow headerRow = sheet.CreateRow(Config.DataHeaderRow - 1);
        headerRow.CreateCell(0).SetCellValue("process failure mode");
        headerRow.CreateCell(1).SetCellValue("rank");
        headerRow.CreateCell(2).SetCellValue("location");

        Dictionary<string, int> map = NpoiEtlUtilities.MapHeaderIndices(sheet);

        Assert.Equal(0, map[Config.DataHeaderNames[0]]);
        Assert.Equal(1, map[Config.DataHeaderNames[1]]);
        Assert.Equal(2, map[Config.DataHeaderNames[2]]);
        Assert.Equal(-1, map[Config.DataHeaderNames[3]]);
    }

    /// <summary>
    /// Verifies that <see cref="NpoiEtlUtilities.GetCellText(IRow?, int)"/> returns empty on a missing row or bad index.
    /// </summary>
    [Fact]
    public void GetCellText_ReturnsEmptyForMissingRowOrInvalidIndex()
    {
        XSSFWorkbook workbook = new ();
        ISheet sheet = workbook.CreateSheet("Sheet1");
        IRow row = sheet.CreateRow(0);
        row.CreateCell(0).SetCellValue("present");

        Assert.Equal(string.Empty, NpoiEtlUtilities.GetCellText(null, 0));
        Assert.Equal(string.Empty, NpoiEtlUtilities.GetCellText(row, -1));
        Assert.Equal("present", NpoiEtlUtilities.GetCellText(row, 0));
    }

    /// <summary>
    /// Verifies that <see cref="NpoiEtlUtilities"/> correctly reports on whether the row is empty.
    /// </summary>
    [Fact]
    public void IsRowEmpty_ReturnsTrueForNullOrBlankRow()
    {
        XSSFWorkbook workbook = new ();
        ISheet sheet = workbook.CreateSheet("Sheet1");
        IRow row = sheet.CreateRow(0);
        row.CreateCell(0).SetCellValue(string.Empty);

        Assert.True(NpoiEtlUtilities.IsRowEmpty(row));
        Assert.True(NpoiEtlUtilities.IsRowEmpty(null));

        row.CreateCell(1000).SetCellValue("contents");
        Assert.False(NpoiEtlUtilities.IsRowEmpty(row));
    }

    /// <summary>
    /// Verifies that <see cref="NpoiEtlUtilities.LoadAndValidateWorkbook"/> throws a format exception when a required column is missing.
    /// </summary>
    /// <returns><inheritdoc/></returns>
    [Fact]
    public async Task LoadAndValidateWorkbook_ThrowsFormatExceptionWhenRequiredColumnMissing()
    {
        string workbookPath = Path.Combine(Path.GetTempPath(), $"fp-{Guid.NewGuid():N}.xlsx");

        XSSFWorkbook workbook = new ();
        ISheet sheet = workbook.CreateSheet("Sheet1");
        IRow metadataRow = sheet.CreateRow(Config.GlobalStartRow - 1);
        metadataRow.CreateCell(NpoiEtlUtilities.ColumnIndex(Config.GlobalColumns[0])).SetCellValue("REV1");
        metadataRow.CreateCell(NpoiEtlUtilities.ColumnIndex(Config.GlobalColumns[1])).SetCellValue("2026-01-15");
        metadataRow.CreateCell(NpoiEtlUtilities.ColumnIndex(Config.GlobalColumns[2])).SetCellValue("Tester");

        IRow headerRow = sheet.CreateRow(Config.DataHeaderRow - 1);
        headerRow.CreateCell(0).SetCellValue(Config.DataHeaderNames[0]);
        headerRow.CreateCell(1).SetCellValue(Config.DataHeaderNames[1]);
        headerRow.CreateCell(2).SetCellValue(Config.DataHeaderNames[2]);

        using (FileStream fs = File.Create(workbookPath))
        {
            workbook.Write(fs);
        }

        await Assert.ThrowsAsync<FormatException>(async () => await NpoiEtlUtilities.LoadAndValidateWorkbook(workbookPath));

        File.Delete(workbookPath);
    }

    /// <summary>
    /// Verifies that the combination load-sheet build-datatable method builds the DataTable correctly.
    /// </summary>
    /// <returns><inheritdoc/></returns>
    [Fact]
    public async Task BuildDataTableFromPath_ParsesWorkbookRowsWithoutDatabase()
    {
        string workbookPath = FpTestWorkbookBuilder.CreateWorkbook();

        try
        {
            DataTable table = await NpoiEtlUtilities.BuildDataTableFromPath(workbookPath, "ALPHA", isFiltering: false, targetColIndex: -1);

            Assert.Equal(2, table.Rows.Count);
            Assert.Equal("Crack", table.Rows[0]["failureMode"]);
            Assert.Equal("A", table.Rows[0]["rank"]);
            Assert.Equal((byte)1, table.Rows[0]["revision"]);
            Assert.Equal((short)12, table.Rows[0]["dummySampleNum"]);
        }
        finally
        {
            File.Delete(workbookPath);
        }
    }

    /// <summary>
    /// Verifies that the combination load-sheet build-datatable method applies a column filter when requested.
    /// </summary>
    /// <returns><inheritdoc/></returns>
    [Fact]
    public async Task BuildDataTableFromPath_AppliesColumnFilterWhenRequested()
    {
        string workbookPath = FpTestWorkbookBuilder.CreateWorkbook();

        try
        {
            DataTable table = await NpoiEtlUtilities.BuildDataTableFromPath(
                workbookPath,
                "ALPHA",
                isFiltering: true,
                targetColIndex: NpoiEtlUtilities.ColumnIndex("BM"));

            Assert.Single(table.Rows);
            Assert.Equal((short)34, table.Rows[0]["dummySampleNum"]);
        }
        finally
        {
            File.Delete(workbookPath);
        }
    }
}
