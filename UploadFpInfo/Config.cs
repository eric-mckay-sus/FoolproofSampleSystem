using StringBuilder = Microsoft.Data.SqlClient.SqlConnectionStringBuilder;

namespace UploadFpInfo;

internal static class Config
{
    // Folder that contains the .xlsx / .xls files to process
    public static string InputLocation  = @"C:\LOCAL NETWORK FILES\XLS";

    // Which worksheet to read from each workbook (0 = first sheet, 1 = second, etc.)
    public static readonly int SheetIndex      = 0;

    // Columns to extract sheet-wide info, specified by Excel letter (e.g. "A", "C", "F").
    // Leave the array empty to automatically extract all columns that have data.
    public static readonly string[] GlobalColumns    = ["AA", "AF", "AM", "AR", "BC"];

    // First row of actual sheet-wide info (1-based). Rows above this are ignored.
    public static readonly int GlobalStartRow    = 4;

    // Row number containing column headers (1-based). Set to 0 to skip headers
    // and auto-generate column letter names (A, B, C...) instead.
    public static readonly int DataHeaderRow   = 7;

    // First row of actual data (1-based). Rows above this are ignored.
    public static readonly int DataStartRow    = 9;

    // Data columns to extract, specified by Excel letter (e.g. "A", "C", "F").
    // Leave the array empty to automatically extract all columns that have data.
    public static readonly string[] DataColumns    = ["A", "G", "AC", "AF"];

    // Number of consecutive fully-empty rows before stopping to read the sheet.
    // Increase this if target data has intentional blank rows within it.
    public static readonly int EmptyRowLimit   = 5;

    public static string DbName => Environment.GetEnvironmentVariable("DB_NAME") ?? "ProductionDB";

    public static string GetConnectionString()
    {
        var builder = new StringBuilder {
            DataSource = Environment.GetEnvironmentVariable("DB_SERVER"),
            UserID = Environment.GetEnvironmentVariable("DB_USER"),
            Password = Environment.GetEnvironmentVariable("DB_PASS"),
            InitialCatalog = DbName,
            TrustServerCertificate = true
        };
        return builder.ConnectionString;
    }
}
