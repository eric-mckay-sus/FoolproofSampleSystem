// <copyright file="ETLUtilities.cs" company="Stanley Electric US Co. Inc.">
// Copyright (c) 2026 Stanley Electric US Co. Inc. Licensed under the MIT License.
// </copyright>

namespace UploadFpInfo;

using NPOI.SS.UserModel;
using System.Globalization;
using System.Data;
using System.Text.RegularExpressions;

/// <summary>
/// Contains utility methods for pre-FP upload parsing.
/// </summary>
public static partial class FPUploadUtilities
{
    /// <summary>
    /// Dynamically maps header names to indices (reads all entries in header row).
    /// </summary>
    /// <param name="sheet">The sheet in which the headers reside.</param>
    /// <returns>The dictionary mapping header names to header indices.</returns>
    public static Dictionary<string, int> MapHeaderIndices(ISheet sheet)
    {
        Dictionary<string, int> map = new (StringComparer.OrdinalIgnoreCase);
        IRow headerRow = sheet.GetRow(Config.DataHeaderRow - 1);

        // Required target columns
        string[] targets = Config.DataHeaderNames;
        foreach (string t in targets)
        {
            map[t] = -1;
        }

        for (int i = 0; i < headerRow.LastCellNum; i++)
        {
            string val = GetCellText(headerRow, i).ToUpper().Trim();
            if (map.ContainsKey(val))
            {
                map[val] = i;
            }
        }

        return map;
    }

    /// <summary>
    /// Get the part master number from an input string
    /// First tries to get a numeric value after the # character, but falls back to any number in the input
    /// If neither work, defaults to null (to denote this entry is irrelvant from a label-making standpoint).
    /// </summary>
    /// <param name="raw">The string to check for part master number.</param>
    /// <returns>The part master number as a short, or DBNull if one does not exist.</returns>
    public static short? ExtractPartNumber(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        Match match = DummySampleExtractor().Match(raw);
        if (match.Success && short.TryParse(match.Groups[1].Value, out short result))
        {
            return result;
        }

        string digits = new (raw.Where(char.IsDigit).ToArray());
        if (!string.IsNullOrEmpty(digits) && short.TryParse(digits, out short fallback))
        {
            return fallback;
        }

        return null;
    }

    /// <summary>
    /// Translates the string with revision number info, handling standard aliases
    /// and clipping REV or R to return just the numeric data.
    /// </summary>
    /// <param name="rev">The string containing revision information.</param>
    /// <returns>A byte representation of the revision number.</returns>
    public static byte TranslateRevString(string rev)
    {
        rev = rev.ToUpper();
        if (rev == "ORIG" || rev == "DRAFT")
        {
            return 0;
        }

        string clean = RevisionNumberCleaner().Replace(rev, string.Empty);
        return byte.TryParse(clean, out byte result) ? result : (byte)0;
    }

    /// <summary>
    /// Constructs a DataTable mappable to the table on SQL Server.
    /// The MaxLength attribute ensures no columns overflow before the server is contacted.
    /// </summary>
    /// <returns>A datatable compliant with the column names and datatypes in the FP table.</returns>
    public static DataTable CreateFoolproofDataTable()
    {
        DataSet ds = new ();
        DataTable dt = ds.Tables.Add("FoolproofInfo");
        dt.Columns.Add("model", typeof(string)).MaxLength = 32;
        dt.Columns.Add("revision", typeof(byte));
        dt.Columns.Add("issueDate", typeof(DateTime));
        dt.Columns.Add("issuer", typeof(string)).MaxLength = 32;
        dt.Columns.Add("failureMode", typeof(string)).MaxLength = 100;
        dt.Columns.Add("rank", typeof(string)).MaxLength = 1;
        dt.Columns.Add("location", typeof(string)).MaxLength = 32;
        dt.Columns.Add("dummySampleNum", typeof(short));

        ds.EnforceConstraints = true;
        return dt;
    }

    /// <summary>
    /// Factored-out method to build and fill a datatable with the necessary data.
    /// </summary>
    /// <param name="sheet">The sheet from which data should be extracted.</param>
    /// <param name="model">The model name to insert.</param>
    /// <param name="revision">The revision number (not iteration number) to insert.</param>
    /// <param name="issueDate">The issue date to insert.</param>
    /// <param name="issuer">The issuer name to insert.</param>
    /// <param name="colMap">The mapping of Excel column names to indices.</param>
    /// <param name="isFiltering">Whether a filter was applied for the first run on this sheet.</param>
    /// <param name="targetColIndex">The target column index of the filter (only rows with data in this column will be inserted).</param>
    /// <returns>The complete DataTable ready for upload.</returns>
    public static DataTable BuildDataTableFromSheet(ISheet sheet, string model, byte revision, DateTime issueDate, string? issuer, Dictionary<string, int> colMap, bool isFiltering, int targetColIndex)
    {
        DataTable dt = CreateFoolproofDataTable();
        int rowIndex = Config.DataStartRow - 1;
        int emptyStreak = 0;

        while (rowIndex <= sheet.LastRowNum && emptyStreak < Config.EmptyRowLimit)
        {
            IRow row = sheet.GetRow(rowIndex);
            if (IsRowEmpty(row))
            {
                emptyStreak++;
                rowIndex++;
                continue;
            }

            emptyStreak = 0;

            short? dummySampleNum = ExtractPartNumber(GetCellText(row, colMap["DUMMY SAMPLE REQUIRED?"]));
            bool passesFilter = !isFiltering || !string.IsNullOrWhiteSpace(GetCellText(row, targetColIndex));

            if (dummySampleNum != null && passesFilter)
            {
                DataRow dr = dt.NewRow();
                dr["model"] = model;
                dr["revision"] = revision;
                dr["issueDate"] = issueDate;
                dr["issuer"] = (object?)issuer ?? DBNull.Value;
                dr["failureMode"] = GetCellText(row, colMap["PROCESS FAILURE MODE"]).Replace("\n", string.Empty);
                dr["rank"] = GetCellText(row, colMap["RANK"]);
                dr["location"] = GetCellText(row, colMap["LOCATION"]);
                dr["dummySampleNum"] = dummySampleNum;
                dt.Rows.Add(dr);
            }

            rowIndex++;
        }

        return dt;
    }

    /// <summary>
    /// Locates and reads the text of a cell with the specified row-col 'coordinates'.
    /// </summary>
    /// <param name="row">The row object containing the desired data (and providing the y-coordinate).</param>
    /// <param name="colIndex">The x-coordinate of the data to get.</param>
    /// <returns>A string of the text in the target cell.</returns>
    public static string GetCellText(IRow? row, int colIndex)
    {
        if (row == null || colIndex < 0)
        {
            return string.Empty;
        }

        ICell cell = row.GetCell(colIndex);
        if (cell == null)
        {
            return string.Empty;
        }

        if (cell.CellType == CellType.Formula)
        {
            return ResolveCellText(cell, cell.CachedFormulaResultType);
        }

        return ResolveCellText(cell, cell.CellType);
    }

    /// <summary>
    /// Reads the data inside a cell object based on its type.
    /// </summary>
    /// <param name="cell">The cell object to read.</param>
    /// <param name="type">The datatype in the cell.</param>
    /// <returns>A string representation of the data in the specified cell.</returns>
    public static string ResolveCellText(ICell cell, CellType type)
    {
        return type switch
        {
            CellType.Numeric => DateUtil.IsCellDateFormatted(cell)
                                ? cell.DateCellValue?.ToString("yyyy-MM-dd") ?? string.Empty
                                : cell.NumericCellValue.ToString(CultureInfo.InvariantCulture),
            CellType.Boolean => cell.BooleanCellValue.ToString(),
            CellType.String => cell.StringCellValue ?? string.Empty,
            _ => string.Empty
        };
    }

    /// <summary>
    /// Verifies whether a row is empty.
    /// </summary>
    /// <param name="row">The row for which to check the contents.</param>
    /// <returns>Whether the row is empty.</returns>
    public static bool IsRowEmpty(IRow? row)
    {
        if (row == null)
        {
            return true;
        }

        return row.Cells.All(c => string.IsNullOrWhiteSpace(ResolveCellText(c, c.CellType == CellType.Formula ? c.CachedFormulaResultType : c.CellType)));
    }

    /// <summary>
    /// Gets the column number (0-based) of an Excel alpha-column index (e.g. ...Y=25, Z=26, AA=27, AB=28)
    /// Returns -1 in the case of the empty string.
    /// </summary>
    /// <remarks>
    /// Excel column enumeration is really just base 26 represented by letters instead of numbers.
    /// </remarks>
    /// <param name="col">The alpha-column index.</param>
    /// <returns>The number column index, or -1 for the empty string.</returns>
    public static int ColumnIndex(string col)
    {
        int index = 0;
        foreach (char c in col.ToUpper())
        {
            index = (index * 26) + c - 'A' + 1;
        }

        return index - 1;
    }

    /// <summary>
    /// Checks the file extension of <paramref name="path"/> to see if it matches one of the Excel formats.
    /// </summary>
    /// <param name="path">The filepath.</param>
    /// <returns>Whether <paramref name="path"/> is an Excel file.</returns>
    public static bool IsExcelFile(string path) =>
        path.EndsWith(".xls", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Gets the revision, issue date and issuer from the file header.
    /// </summary>
    /// <param name="sheet">The sheet to be parsed.</param>
    /// <returns>A tuple containing the desired metadata.</returns>
    public static (byte Revision, DateTime IssueDate, string Issuer) ParseMetadata(ISheet sheet)
    {
        IRow dataRow = sheet.GetRow(Config.GlobalStartRow - 1);
        int[] metadataIndices = Config.GlobalColumns.Select(ColumnIndex).ToArray();

        string revRaw = GetCellText(dataRow, metadataIndices[0]);
        string dateRaw = GetCellText(dataRow, metadataIndices[1]);
        string issuer = GetCellText(dataRow, metadataIndices[2]);

        byte revision = TranslateRevString(revRaw);

        // Clean common Excel date string artifacts
        string cleanDate = dateRaw.Replace("th", string.Empty, StringComparison.OrdinalIgnoreCase)
                                  .Replace("st", string.Empty, StringComparison.OrdinalIgnoreCase)
                                  .Replace("nd", string.Empty, StringComparison.OrdinalIgnoreCase)
                                  .Replace("rd", string.Empty, StringComparison.OrdinalIgnoreCase);

        if (!DateTime.TryParse(cleanDate, out DateTime issueDate))
        {
            issueDate = DateTime.MinValue;
        }

        return (revision, issueDate, issuer);
    }

    // Generating the regular expressions at compile-time expedites the match
    [GeneratedRegex(@"#(\d+)")]
    private static partial Regex DummySampleExtractor();

    [GeneratedRegex("[REV|R]")]
    private static partial Regex RevisionNumberCleaner();
}
