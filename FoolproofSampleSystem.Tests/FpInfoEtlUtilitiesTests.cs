// <copyright file="FpInfoEtlUtilitiesTests.cs" company="Stanley Electric US Co. Inc.">
// Copyright (c) 2026 Stanley Electric US Co. Inc. Licensed under the MIT License.
// </copyright>

namespace FoolproofSampleSystem.Tests;

using System.Data;
using UploadFpInfo;

public sealed class FpInfoEtlUtilitiesTests
{
    [Theory]
    [InlineData("#12 required", (short)12)]
    [InlineData("dummy sample 34", (short)34)]
    [InlineData("none", null)]
    [InlineData("", null)]
    public void ExtractPartNumber_ReturnsExpectedNumber(string raw, short? expected)
    {
        Assert.Equal(expected, NpoiEtlUtilities.ExtractPartNumber(raw));
    }

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

    [Fact]
    public void CreateFoolproofDataTable_EnforcesColumnTypesAndLengthsInMemory()
    {
        DataTable table = NpoiEtlUtilities.CreateFoolproofDataTable();

        Assert.Equal(typeof(byte), table.Columns["revision"]!.DataType);
        Assert.Equal(typeof(short), table.Columns["dummySampleNum"]!.DataType);
        Assert.Equal(32, table.Columns["model"]!.MaxLength);
        Assert.Equal(1, table.Columns["rank"]!.MaxLength);
    }

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

    [Theory]
    [InlineData("sheet.xlsx", true)]
    [InlineData("sheet.xls", true)]
    [InlineData("sheet.csv", false)]
    public void IsExcelFile_DetectsSupportedExtensions(string path, bool expected)
    {
        Assert.Equal(expected, NpoiEtlUtilities.IsExcelFile(path));
    }

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
