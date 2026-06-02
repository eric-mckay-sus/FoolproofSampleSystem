// <copyright file="FpTestWorkbookBuilder.cs" company="Stanley Electric US Co. Inc.">
// Copyright (c) 2026 Stanley Electric US Co. Inc. Licensed under the MIT License.
// </copyright>

namespace FoolproofSampleSystem.Tests;

using InterProcessIO;
using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;

using static UploadFpInfo.NpoiEtlUtilities;

/// <summary>
/// Excel workbook creator for testing the FP data sheet uploader.
/// </summary>
internal static class FpTestWorkbookBuilder
{
    /// <summary>
    /// Creates a Excel workbook with a header constructed from the necessary information.
    /// </summary>
    /// <param name="revision">The revision to use.</param>
    /// <param name="issueDate">The issue date to use.</param>
    /// <param name="issuer">The issuer name to use.</param>
    /// <param name="rows">The number of blank rows to add.</param>
    /// <returns>The path to the new workbook.</returns>
    public static string CreateWorkbook(
        string revision = "REV1",
        string issueDate = "2026-01-15",
        string issuer = "Tester",
        IEnumerable<(string FailureMode, string Rank, string Location, string DummySample, string? FilterMarker)>? rows = null)
    {
        XSSFWorkbook workbook = new ();
        ISheet sheet = workbook.CreateSheet("Sheet1");

        WriteMetadataRow(sheet, revision, issueDate, issuer);
        Dictionary<string, int> columnMap = WriteHeaderRow(sheet);

        int rowIndex = Config.DataStartRow - 1;
        foreach ((string failureMode, string rank, string location, string dummySample, string? filterMarker) in rows ?? DefaultRows())
        {
            IRow row = sheet.CreateRow(rowIndex++);
            row.CreateCell(columnMap[Config.DataHeaderNames[0]]).SetCellValue(failureMode);
            row.CreateCell(columnMap[Config.DataHeaderNames[1]]).SetCellValue(rank);
            row.CreateCell(columnMap[Config.DataHeaderNames[2]]).SetCellValue(location);
            row.CreateCell(columnMap[Config.DataHeaderNames[3]]).SetCellValue(dummySample);

            if (filterMarker != null)
            {
                row.CreateCell(ColumnIndex("BM")).SetCellValue(filterMarker);
            }
        }

        string path = Path.Combine(Path.GetTempPath(), $"fp-{Guid.NewGuid():N}.xlsx");
        using FileStream stream = new (path, FileMode.Create, FileAccess.Write);
        workbook.Write(stream);
        return path;
    }

    /// <summary>
    /// Gets a list of default row data of the correct shape.
    /// </summary>
    /// <returns>The list of default row data.</returns>
    private static IEnumerable<(string, string, string, string, string?)> DefaultRows() =>
    [
        ("Crack", "A", "Press 1", "#12 required", null),
        ("Warp", "B", "Press 2", "dummy sample 34", "X"),
    ];

    /// <summary>
    /// Writes metadata to the correct location in the provided <paramref name="sheet"/>.
    /// </summary>
    /// <param name="sheet">The ISheet instance to write to.</param>
    /// <param name="revision">The revision number to use.</param>
    /// <param name="issueDate">The issue date to use.</param>
    /// <param name="issuer">The issuer name to use.</param>
    private static void WriteMetadataRow(ISheet sheet, string revision, string issueDate, string issuer)
    {
        IRow row = sheet.CreateRow(Config.GlobalStartRow - 1);
        row.CreateCell(ColumnIndex(Config.GlobalColumns[0])).SetCellValue(revision);
        row.CreateCell(ColumnIndex(Config.GlobalColumns[1])).SetCellValue(issueDate);
        row.CreateCell(ColumnIndex(Config.GlobalColumns[2])).SetCellValue(issuer);
    }

    /// <summary>
    /// Writes data header rows to the correct location in the provided <paramref name="sheet"/>.
    /// </summary>
    /// <param name="sheet">The ISheet instance to write to.</param>
    /// <returns>The dictionary mapping header names to indices.</returns>
    private static Dictionary<string, int> WriteHeaderRow(ISheet sheet)
    {
        IRow row = sheet.CreateRow(Config.DataHeaderRow - 1);
        for (int i = 0; i < Config.DataHeaderNames.Length; i++)
        {
            row.CreateCell(i).SetCellValue(Config.DataHeaderNames[i]);
        }

        row.CreateCell(ColumnIndex("BM")).SetCellValue("FILTER");
        return MapHeaderIndices(sheet);
    }
}
