namespace ParseSampleSheet;

internal static class Config
{
    // Folder that contains the .xlsx / .xls files to process
    public static string InputFolder  = @"C:\LOCAL NETWORK FILES\XLS";

    // Folder where individual CSV files will be written (created if it doesn't exist).
    // Each Excel file produces:  <OutputFolder>\<original-name>.csv
    public static string OutputFolder = @"C:\LOCAL NETWORK FILES\CSV";

    // Which worksheet to read from each workbook (0 = first sheet, 1 = second, etc.)
    public static int SheetIndex      = 0;

    // Columns to extract sheet-wide info, specified by Excel letter (e.g. "A", "C", "F").
    // Leave the array empty to automatically extract all columns that have data.
    public static string[] GlobalColumns    = ["AA", "AF", "AM", "AR", "BC"];

    // Row number containing column headers (1-based) for sheet-wide info. Set to 0 to skip headers
    // and auto-generate column letter names (A, B, C...) instead.
    public static int GlobalHeaderRow   = 2;

    // First row of actual sheet-wide info (1-based). Rows above this are ignored.
    public static int GlobalStartRow    = 4;

    // Row number containing column headers (1-based). Set to 0 to skip headers
    // and auto-generate column letter names (A, B, C...) instead.
    public static int DataHeaderRow   = 7;

    // First row of actual data (1-based). Rows above this are ignored.
    public static int DataStartRow    = 9;

    // Data columns to extract, specified by Excel letter (e.g. "A", "C", "F").
    // Leave the array empty to automatically extract all columns that have data.
    public static string[] DataColumns    = ["A", "G", "AC", "AF"];

    // Number of consecutive fully-empty rows before stopping to read the sheet.
    // Increase this if target data has intentional blank rows within it.
    public static int EmptyRowLimit   = 5;
}
