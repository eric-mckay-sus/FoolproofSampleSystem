using System.Text;
using NPOI.HSSF.UserModel;
using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;

namespace ParseSampleSheet;

public class Program
{
    /// <summary>
    /// Entry point. Calls Run() and catches any unhandled exceptions, printing them in red before exiting with a non-zero code so that calling scripts or task schedulers can detect the failure.
    /// </summary>
    /// <param name="args"></param>
    public static void Main(string[] args)
    {
        try
        {
            // If there is a command line argument, try to use it as the file/folder in case the user wants to override the config
            if (args.Length != 0){
                string fileName = args[0];
                // First try treating it as a single Excel file
                if (File.Exists(fileName) && Path.GetExtension(fileName).Contains("xls", StringComparison.OrdinalIgnoreCase))
                {
                    string csvName = Path.GetFileNameWithoutExtension(fileName) + ".csv";
                    string csvPath = Path.Combine(Config.OutputFolder, csvName);
                    int numRows = ProcessFile(fileName, csvPath);
                    Console.WriteLine("Parse successful.");
                    Console.WriteLine($"  {Path.GetFileName(fileName)}  ->  {csvName} ... {numRows} data row(s)");
                    return;
                }
                else if (Directory.Exists(fileName)) // If it's not a file, it has to be a directory to be valid
                {
                    Config.InputFolder = fileName;
                }
                else // If it isn't a directory, we can't use it, so fall back to the default
                {
                    PrintInRed($"{fileName} could not be found as a folder. Defaulting to the config folder ({Config.InputFolder})");
                }
            }
            ParseBatch();
        }
        catch (Exception ex)
        {
            PrintInRed($"Fatal error: {ex.Message}");
            return;
        }
    }

    /// <summary>
    /// Prints a message in red to the console
    /// </summary>
    /// <param name="message">The message to print</param>
    private static void PrintInRed(string message)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.Error.WriteLine(message);
        Console.ResetColor();
    }

    /// <summary>
    /// Scans the input folder for Excel files, then processes each one
    /// in alphabetical order, writing a separate CSV per file.
    /// </summary>
    public static void ParseBatch()
    {
        // Verify the input folder exists before doing anything else
        var inputDir = new DirectoryInfo(Config.InputFolder);
        if (!inputDir.Exists)
            throw new DirectoryNotFoundException($"Input folder not found: {Config.InputFolder}");

        // Create the output folder if it doesn't already exist
        Directory.CreateDirectory(Config.OutputFolder);

        // Collect all .xlsx and .xls files, sorted alphabetically so
        // processing order is predictable regardless of file system order
        var files = inputDir.GetFiles("*.xlsx")
                            .Concat(inputDir.GetFiles("*.xls"))
                            .OrderBy(f => f.Name)
                            .ToArray();

        if (files.Length == 0)
        {
            Console.WriteLine("No Excel files found in the input folder.");
            return;
        }

        Console.WriteLine($"Found {files.Length} Excel file(s) -> {Config.OutputFolder}\\\n");

        int totalRows = 0;
        int skipped = 0;

        // Process each Excel file independently. If a single file fails
        // (e.g. corrupted or password-protected), it is skipped with a
        // warning and processing continues with the remaining files.
        foreach (var file in files)
        {
            // Output CSV gets the same base name as the source Excel file
            string csvName = Path.GetFileNameWithoutExtension(file.Name) + ".csv";
            string csvPath = Path.Combine(Config.OutputFolder, csvName);

            Console.Write($"  {file.Name}  ->  {csvName} ... ");
            try
            {
                int rows = ProcessFile(file.FullName, csvPath);
                Console.WriteLine($"{rows} data row(s)");
                totalRows += rows;
            }
            catch (Exception ex)
            {
                // Highlight skipped files in yellow so they are easy to spot
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"SKIPPED ({ex.Message})");
                Console.ResetColor();
                skipped++;
            }
        }

        Console.WriteLine($"\nDone. {files.Length - skipped} CSV file(s) written, " +
                            $"{totalRows} total data row(s). ({skipped} skipped)");
    }

    /// <summary>
    /// Opens a single Excel file, reads the configured sheet, and writes
    /// the extracted data to a CSV file. Returns the number of data rows written.
    /// </summary>
    public static int ProcessFile(string excelPath, string csvPath)
    {
        // Open the file as a read-only stream to avoid locking it.
        // NPOI uses different workbook classes for the two Excel formats:
        //   XSSFWorkbook = .xlsx (Open XML, Excel 2007+)
        //   HSSFWorkbook = .xls  (OLE compound document, Excel 97-2003)
        IWorkbook workbook;
        using (var fs = new FileStream(excelPath, FileMode.Open, FileAccess.Read))
        {
            workbook = excelPath.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase)
                ? new XSSFWorkbook(fs)
                : new HSSFWorkbook(fs);
        }

        // Get the target worksheet by its 0-based index (Config.SheetIndex 0 = first sheet)
        var sheet = workbook.GetSheetAt(Config.SheetIndex)
            ?? throw new InvalidOperationException($"Sheet index {Config.SheetIndex} not found.");

        // Determine which column indices to extract (0-based for NPOI)
        int[] globalColIndices = ResolveGlobalColumns();
        int[] dataColIndices = ResolveDataColumns(sheet);

        if (dataColIndices.Length == 0)
            throw new InvalidOperationException("No columns found to extract.");

        // Open the output CSV for writing, overwriting any existing file
        using var writer = new StreamWriter(csvPath, append: false, Encoding.UTF8);

        // Handle the global info (model, product, rev, date, issuer)
        if (Config.GlobalHeaderRow > 0 || Config.GlobalStartRow > 0)
        {
            writer.WriteLine("# --- GLOBAL METADATA ---");

            // Write Global Headers
            if (Config.GlobalHeaderRow > 0)
            {
                IRow gHeaderRow = sheet.GetRow(Config.GlobalHeaderRow - 1);
                IEnumerable<string> gHeaders = globalColIndices.Select(col => CsvEscape(GetCellText(gHeaderRow, col)));
                writer.WriteLine("# " + string.Join(",", gHeaders));
            }

            // Write Global Data Rows (usually just one row, but handles multiple)
            var row = sheet.GetRow(Config.GlobalStartRow - 1);
            var values = globalColIndices.Select(col => GetCellText(row, col)).ToArray();

            if (values.Any(v => !string.IsNullOrWhiteSpace(v)))
            {
                writer.WriteLine("# " + string.Join(",", values.Select(CsvEscape)));
            }
            writer.WriteLine("# --- END METADATA ---");
        }

        // Write the data header row first, if one is configured.
        // Config.DataHeaderRow is 1-based; NPOI uses 0-based row indices, so subtract 1.
        // If DataHeaderRow is 0 (disabled), fall back to auto-generated column letters (A, B, C...).
        if (Config.DataHeaderRow > 0)
        {
            var headerRow = sheet.GetRow(Config.DataHeaderRow - 1);
            var headers = dataColIndices.Select(col => CsvEscape(GetCellText(headerRow, col)));
            writer.WriteLine(string.Join(",", headers));
        }
        else
        {
            // No header row configured — generate column letter names instead
            writer.WriteLine(string.Join(",", dataColIndices.Select(ColumnLetter)));
        }

        // Track consecutive empty rows so we know when to stop reading.
        // This avoids reading thousands of blank rows to the end of the sheet.
        int rowsWritten = 0;
        int emptyStreak = 0;
        int rowIndex = Config.DataStartRow - 1; // convert Config's 1-based row to 0-based
        int lastRow = sheet.LastRowNum;          // NPOI's last physical row in the sheet

        while (rowIndex <= lastRow && emptyStreak < Config.EmptyRowLimit)
        {
            // Read the configured columns from the current row
            var row = sheet.GetRow(rowIndex);
            var values = dataColIndices.Select(col => GetCellText(row, col)).ToArray();

            // If every extracted cell is blank, increment the empty streak counter
            // and move on without writing anything to the CSV
            if (values.All(v => string.IsNullOrWhiteSpace(v)))
            {
                emptyStreak++;
                rowIndex++;
                continue;
            }

            // Non-empty row found — reset the streak counter and write the row
            emptyStreak = 0;
            writer.WriteLine(string.Join(",", values.Select(CsvEscape)));
            rowsWritten++;
            rowIndex++;
        }

        return rowsWritten;
    }

    /// <summary>
    /// Retrieves the text value of a specific cell in a row.
    /// Returns an empty string if the row or cell is null.
    /// </summary>
    /// <param name="row">The row (y-coordinate) of the cell</param>
    /// <param name="colIndex">The column (x-coordinate) of the cell</param>
    public static string GetCellText(IRow? row, int colIndex)
    {
        if (row == null) return "";
        ICell cell = row.GetCell(colIndex);
        if (cell == null) return "";

        // Delegate to ResolveCellText so formula cells can be handled
        // recursively using their cached result type
        return ResolveCellText(cell, cell.CellType);
    }

    /// <summary>
    /// Converts an NPOI cell value to a plain string based on its cell type.
    /// Formula cells are resolved by recursing with their cached result type,
    /// so the final computed value is returned rather than the formula itself.
    /// </summary>
    /// <param name="cell">The cell to resolve the text for</param>
    /// <param name="cellType">The datatype contained in the cell</param>
    static string ResolveCellText(ICell cell, CellType cellType)
    {
        switch (cellType)
        {
            case CellType.Numeric:
                // Numeric cells store both regular numbers and dates as doubles.
                // Check the cell's format to distinguish dates from plain numbers.
                if (DateUtil.IsCellDateFormatted(cell))
                {
                    DateTime? dt = cell.DateCellValue;
                    return dt.HasValue ? dt.Value.ToString("yyyy-MM-dd") : "";
                }
                return cell.NumericCellValue.ToString();

            case CellType.Boolean:
                return cell.BooleanCellValue.ToString();

            case CellType.Formula:
                // Formula cells store the last calculated result alongside the
                // formula string. Recurse using the cached result type so we
                // return the computed value (number, string, bool) not "=SUM(...)".
                return ResolveCellText(cell, cell.CachedFormulaResultType);

            case CellType.Error:
                // Error cells (e.g. #DIV/0!, #REF!) are written as empty string
                return "";

            default:
                // Blank and String cells — StringCellValue can throw on truly
                // blank cells in some NPOI versions, so wrap in try/catch
                try { return cell.StringCellValue ?? ""; }
                catch { return ""; }
        }
    }

    /// <summary>
    /// Resolves the column indices specifically for the Global metadata section.
    /// If none are provided, exclude this section
    /// </summary>
    static int[] ResolveGlobalColumns()
    {
        if (Config.GlobalColumns.Length > 0)
        {
            return Config.GlobalColumns
                        .Select(c => ColumnIndex(c.Trim().ToUpperInvariant()) - 1)
                        .Where(i => i >= 0)
                        .ToArray();
        }
        return Array.Empty<int>();
    }

    /// <summary>
    /// Returns the 0-based column indices to extract, either from the
    /// configured column letters or by auto-detecting all used columns.
    /// </summary>
    /// <param name="sheet">The Excel sheet from which columns should be extracted</param>
    static int[] ResolveDataColumns(ISheet sheet)
    {
        if (Config.DataColumns.Length > 0)
        {
            // Convert each configured letter (e.g. "A", "C") to a 0-based NPOI
            // column index. ColumnIndex returns a 1-based value, so subtract 1.
            return Config.DataColumns
                            .Select(c => ColumnIndex(c.Trim().ToUpperInvariant()) - 1)
                            .Where(i => i >= 0)
                            .ToArray();
        }

        // No columns configured — scan the header and first data rows to find
        // the rightmost used column and include everything up to that point
        int maxCol = 0;
        for (int r = 0; r <= Math.Min(sheet.LastRowNum, Config.DataStartRow + 10); r++)
        {
            var row = sheet.GetRow(r);
            if (row != null && row.LastCellNum > maxCol)
                maxCol = row.LastCellNum;
        }
        return Enumerable.Range(0, maxCol).ToArray();
    }

    /// <summary>
    /// Converts an Excel column letter (or letters) to a 1-based column index.
    /// Examples: A=1, Z=26, AA=27, AB=28
    /// </summary>
    /// </param name="col">The column letter to convert to column number</param>
    public static int ColumnIndex(string col)
    {
        int index = 0;
        foreach (char c in col)
        {
            if (!char.IsLetter(c)) return 0;
            index = index * 26 + c - 'A' + 1;
        }
        return index;
    }

    /// <summary>
    /// Converts a 0-based NPOI column index back to an Excel column letter.
    /// Used when auto-generating header names (e.g. 0=A, 1=B, 26=AA).
    /// </summary>
    /// <param name="col">The column number to convert to column letter</param>
    public static string ColumnLetter(int col)
    {
        col++; // NPOI is 0-based; the conversion algorithm expects 1-based
        string result = "";
        while (col > 0)
        {
            col--;
            result = (char)('A' + col % 26) + result;
            col /= 26;
        }
        return result;
    }

    /// <summary>
    /// Escapes a value for safe inclusion in a CSV file (RFC 4180).
    /// Newline characters within cell data are replaced with a space to
    /// prevent them from breaking the single-row-per-record structure of the CSV.
    /// Values containing commas or double-quotes are wrapped in double-quotes,
    /// with any embedded double-quotes doubled up ("").
    /// </summary>
    /// <param name="value">The value to escape</param>
    public static string CsvEscape(string? value)
    {
        if (value is null) return "";

        // Replace all newline variants with a space so multi-line cell content
        // does not split across multiple rows in the CSV output.
        // Order matters: replace \r\n (Windows) before \r or \n individually.
        value = value.Replace("\r\n", " ").Replace("\r", " ").Replace("\n", " ");

        // Wrap in double-quotes if the value contains a comma or a double-quote,
        // and escape any embedded double-quotes by doubling them
        bool needsQuotes = value.Contains(',') || value.Contains('"');
        if (!needsQuotes) return value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
}
