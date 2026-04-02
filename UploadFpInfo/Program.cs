using System.Data;
using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using NPOI.HSSF.UserModel;
using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;

namespace UploadFpInfo;

/// <summary>
/// Consolidates the parse/upload process for foolproof dummy sample sheets
/// </summary>
public class UploadFoolproofToDb
{
    /// <summary>
    /// Main entry point: detect input location argument and delegate to file/batch handler based on whether input location is file or folder
    /// </summary>
    /// <param name="args">Command line arguments, accepts 0-1</param>
    /// <returns></returns>
    public static async Task Main(string[] args)
    {
        try
        {
            string path = args.Length > 0 ? args[0] : Config.InputLocation;

            if (Directory.Exists(path))
            {
                Config.InputLocation = path;
            }
            else if (File.Exists(path) && IsExcelFile(path))
            {
                await ProcessSingleFile(path);
                return;
            }
            else
            {
                PrintInColor($"Path '{path}' is not a valid directory or Excel file. Using Config default ({Config.InputLocation}).", ConsoleColor.Yellow);
            }

            await RunBatch();
        }
        catch (Exception ex)
        {
            PrintInColor($"Fatal error: {ex.Message}", ConsoleColor.Red);
        }
    }

    /// <summary>
    /// Process a batch of FP info files
    /// </summary>
    /// <returns></returns>
    /// <exception cref="DirectoryNotFoundException">When the input location does not exist</exception>
    private static async Task RunBatch()
    {
        var inputDir = new DirectoryInfo(Config.InputLocation);
        if (!inputDir.Exists) throw new DirectoryNotFoundException(Config.InputLocation);

        FileInfo[] files = inputDir.GetFiles("*.xlsx")
                            .Concat(inputDir.GetFiles("*.xls"))
                            .OrderBy(f => f.Name)
                            .ToArray();

        if (files.Length == 0)
        {
            Console.WriteLine("No Excel files found.");
            return;
        }

        Console.WriteLine($"Found {files.Length} files. Starting upload to {Config.DbName}...");

        foreach (FileInfo file in files)
        {
            try
            {
                await ProcessSingleFile(file.FullName);
            }
            catch (Exception ex)
            {
                PrintInColor($"[SKIP] {file.Name}: {ex.Message}", ConsoleColor.Yellow);
            }
        }
    }

    /// <summary>
    /// Process one FP info file
    /// </summary>
    /// <param name="excelPath">The path to the file to be processed</param>
    /// <returns></returns>
    /// <exception cref="Exception">When the file does not have a sheet at the specified index</exception>
    private static async Task ProcessSingleFile(string excelPath)
    {
        Console.Write($"Processing {Path.GetFileName(excelPath)}... ");

        // Load Excel file
        IWorkbook workbook;
        using (var fs = new FileStream(excelPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            workbook = excelPath.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase)
                ? new XSSFWorkbook(fs)
                : new HSSFWorkbook(fs);
        }

        ISheet sheet = workbook.GetSheetAt(Config.SheetIndex)
                    ?? throw new Exception($"Sheet index {Config.SheetIndex} not found.");

        // Extract metadata (header row)
        (string? Model, byte Revision, DateTime IssueDate, string? Issuer) = ParseMetadata(sheet);

        // Get column indices associated with column names
        Dictionary<string, int> colMap = MapHeaderIndices(sheet);

        // Initialize DataTable for rows
        DataTable dt = CreateFoolproofDataTable();
        int rowIndex = Config.DataStartRow - 1;
        int emptyStreak = 0;
        int rowsProcessed = 0;

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
            DataRow dr = dt.NewRow();

            // Assign metadata
            dr["model"] = Model;
            dr["revision"] = Revision;
            dr["issueDate"] = IssueDate;
            dr["issuer"] = (object?)Issuer ?? DBNull.Value;

            // Get the data for
            dr["failureMode"] = GetCellText(row, colMap["PROCESS FAILURE MODE"]);
            dr["rank"] = GetCellText(row, colMap["RANK"]);
            dr["location"] = GetCellText(row, colMap["LOCATION"]);
            dr["partMasterNum"] = ExtractPartNumber(GetCellText(row, colMap["DUMMY SAMPLE REQUIRED?"]));

            dt.Rows.Add(dr);
            rowsProcessed++;
            rowIndex++;
        }

        // Bulk upload to SQL
        if (dt.Rows.Count > 0)
        {
            await WriteToDatabase(dt);
            Console.WriteLine($"Uploaded {rowsProcessed} rows.");
        }
        else
        {
            Console.WriteLine("No data rows found.");
        }
    }

    /// <summary>
    /// Gets the model, revision, issue date and issuer from the file header
    /// </summary>
    /// <param name="sheet">The sheet to be parsed</param>
    /// <returns>A tuple containing the desired metadata</returns>
    private static (string Model, byte Revision, DateTime IssueDate, string Issuer) ParseMetadata(ISheet sheet)
    {
        IRow dataRow = sheet.GetRow(Config.GlobalStartRow - 1);
        int[] globalIndices = Config.GlobalColumns.Select(c => ColumnIndex(c) - 1).ToArray();

        string baseModel = GetCellText(dataRow, globalIndices[0]);
        string product = GetCellText(dataRow, globalIndices[1]);
        string revRaw = GetCellText(dataRow, globalIndices[2]);
        string dateRaw = GetCellText(dataRow, globalIndices[3]);
        string issuer = GetCellText(dataRow, globalIndices[4]);

        string model = $"{baseModel} {product}".Trim();
        byte revision = TranslateRevString(revRaw);

        // Clean common Excel date string artifacts
        string cleanDate = dateRaw.Replace("th", "", StringComparison.OrdinalIgnoreCase)
                                  .Replace("st", "", StringComparison.OrdinalIgnoreCase)
                                  .Replace("nd", "", StringComparison.OrdinalIgnoreCase)
                                  .Replace("rd", "", StringComparison.OrdinalIgnoreCase);

        if (!DateTime.TryParse(cleanDate, out DateTime issueDate))
            issueDate = DateTime.MinValue;

        return (model, revision, issueDate, issuer);
    }

    /// <summary>
    /// Creates a dictionary mapping header names to indices
    /// </summary>
    /// <param name="sheet"></param>
    /// <returns></returns>
    private static Dictionary<string, int> MapHeaderIndices(ISheet sheet)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        IRow headerRow = sheet.GetRow(Config.DataHeaderRow - 1);

        // Required target columns
        string[] targets = ["PROCESS FAILURE MODE", "RANK", "LOCATION", "DUMMY SAMPLE REQUIRED?"];
        foreach (string t in targets) map[t] = -1;

        for (int i = 0; i < headerRow.LastCellNum; i++)
        {
            string val = GetCellText(headerRow, i).ToUpper().Trim();
            if (map.ContainsKey(val)) map[val] = i;
        }

        return map;
    }

    /// <summary>
    /// Get the part master number from an input string
    /// First tries to get a numeric value after the # character, but falls back to any number in the input
    /// If neither work, defaults to DB null
    /// </summary>
    /// <param name="raw">The string to check for part master number</param>
    /// <returns>The part master number as a short, or DBNull if one does not exist</returns>
    private static object ExtractPartNumber(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return DBNull.Value;

        Match match = Regex.Match(raw, @"#(\d+)");
        if (match.Success && short.TryParse(match.Groups[1].Value, out short result))
            return result;

        string digits = new(raw.Where(char.IsDigit).ToArray());
        if (!string.IsNullOrEmpty(digits) && short.TryParse(digits, out short fallback))
            return fallback;

        return DBNull.Value;
    }

    /// <summary>
    /// Asynchronously writes the input DataTable's contents to the FP info table
    /// </summary>
    /// <param name="dt">The DataTable whose contents will be written to the server</param>
    /// <returns></returns>
    private static async Task WriteToDatabase(DataTable dt)
    {
        using var conn = new SqlConnection(Config.GetConnectionString());
        await conn.OpenAsync();
        using var bulk = new SqlBulkCopy(conn);
        bulk.DestinationTableName = "dbo.FoolproofInfo";

        foreach (DataColumn col in dt.Columns)
            bulk.ColumnMappings.Add(col.ColumnName, col.ColumnName);

        await bulk.WriteToServerAsync(dt);
    }

    /// <summary>
    /// Translates the string with revision number info, handling standard aliases
    /// and clipping REV or R to return just the numeric data
    /// </summary>
    /// <param name="rev">The string containing revision information</param>
    /// <returns>A byte representation of the revision number</returns>
    private static byte TranslateRevString(string rev)
    {
        rev = rev.ToUpper();
        if (rev == "ORIG" || rev == "DRAFT") return 0;
        string clean = Regex.Replace(rev, "[REV|R]", "");
        return byte.TryParse(clean, out byte result) ? result : (byte)0;
    }

    /// <summary>
    /// Constructs a DataTable mappable to the table on SQL Server
    /// </summary>
    /// <returns>A datatable compliant with the column names and datatypes in the FP table</returns>
    private static DataTable CreateFoolproofDataTable()
    {
        DataTable dt = new();
        dt.Columns.Add("model", typeof(string));
        dt.Columns.Add("revision", typeof(byte));
        dt.Columns.Add("issueDate", typeof(DateTime));
        dt.Columns.Add("issuer", typeof(string));
        dt.Columns.Add("failureMode", typeof(string));
        dt.Columns.Add("rank", typeof(string));
        dt.Columns.Add("location", typeof(string));
        dt.Columns.Add("partMasterNum", typeof(short));
        return dt;
    }

    /// <summary>
    /// Locates and reads the text of a cell with the specified row-col 'coordinates'
    /// </summary>
    /// <param name="row">The row object containing the desired data (and providing the y-coordinate)</param>
    /// <param name="colIndex">The x-coordinate of the data to get</param>
    /// <returns>A string of the text in the target cell</returns>
    private static string GetCellText(IRow? row, int colIndex)
    {
        if (row == null || colIndex < 0) return "";
        ICell cell = row.GetCell(colIndex);
        if (cell == null) return "";

        if (cell.CellType == CellType.Formula)
            return ResolveCellText(cell, cell.CachedFormulaResultType);

        return ResolveCellText(cell, cell.CellType);
    }

    /// <summary>
    /// Reads the data inside a cell object based on its type
    /// </summary>
    /// <param name="cell">The cell object to read</param>
    /// <param name="type">The datatype in the cell</param>
    /// <returns>A string representation of the data in the specified cell</returns>
    private static string ResolveCellText(ICell cell, CellType type)
    {
        return type switch
        {
            CellType.Numeric => DateUtil.IsCellDateFormatted(cell)
                                ? cell.DateCellValue?.ToString("yyyy-MM-dd") ?? ""
                                : cell.NumericCellValue.ToString(CultureInfo.InvariantCulture),
            CellType.Boolean => cell.BooleanCellValue.ToString(),
            CellType.String => cell.StringCellValue ?? "",
            _ => ""
        };
    }

    /// <summary>
    /// Verifies whether a row is empty
    /// </summary>
    /// <param name="row">The row for which to check the contents</param>
    /// <returns>Whether the row is empty</returns>
    private static bool IsRowEmpty(IRow? row)
    {
        if (row == null) return true;
        return row.Cells.All(c => string.IsNullOrWhiteSpace(ResolveCellText(c, c.CellType == CellType.Formula ? c.CachedFormulaResultType : c.CellType)));
    }

    /// <summary>
    /// Gets the column number (1-based) of an Excel alpha-column index (e.g. ...Y=25, Z=26, AA=27, AB=28)
    /// It's just base 26 plus 1 represented by letters instead of numbers
    /// </summary>
    /// <param name="col">The alpha-column index</param>
    /// <returns>The number column index</returns>
    private static int ColumnIndex(string col)
    {
        int index = 0;
        foreach (char c in col.ToUpper()) index = index * 26 + c - 'A' + 1;
        return index;
    }

    /// <summary>
    /// Checks the input file's extension to see if it matches one of the Excel formats
    /// </summary>
    /// <param name="path">The filepath</param>
    /// <returns>Whether <paramref name="path"/> is an Excel file</returns>
    private static bool IsExcelFile(string path) =>
        path.EndsWith(".xls", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Prints <paramref name="msg"/> in the specified <paramref name="color"/>
    /// </summary>
    /// <param name="msg">The text to be printed</param>
    /// <param name="color">The color in which to print</param>
    private static void PrintInColor(string msg, ConsoleColor color)
    {
        Console.ForegroundColor = color;
        Console.WriteLine(msg);
        Console.ResetColor();
    }
}
