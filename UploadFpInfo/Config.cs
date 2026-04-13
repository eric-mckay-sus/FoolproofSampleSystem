// <copyright file="Config.cs" company="Stanley Electric US Co. Inc.">
// Copyright (c) 2026 Stanley Electric US Co. Inc. Licensed under the MIT License.
// </copyright>
namespace UploadFpInfo;

using StringBuilder = Microsoft.Data.SqlClient.SqlConnectionStringBuilder;

/// <summary>
/// A container for the data that is constant in UploadFpInfo (but could change).
/// </summary>
internal static class Config
{
    /// <summary>
    /// Gets or sets the folder that contains the .xlsx / .xls files to process.
    /// </summary>
    public static string InputLocation { get; set; } = @"P:\PE III\1-Standard Documents\FPM\+++SHEETS FOR DATABASE TESTING";

    /// <summary>
    /// Gets the (0-based) worksheet to read from each workbook.
    /// </summary>
    public static int SheetIndex { get; } = 0;

    /// <summary>
    /// Gets the columns which hold sheet-wide info, specified by Excel letter (e.g. "A", "C", "F").
    /// Leave the array empty to automatically extract all columns that have data.
    /// </summary>
    public static string[] GlobalColumns { get; } = ["AM", "AR", "BC"];

    /// <summary>
    /// Gets the first row of actual sheet-wide info (1-based). Rows above this are ignored.
    /// </summary>
    public static int GlobalStartRow { get; } = 4;

    /// <summary>
    /// Gets the row number containing column headers (1-based). Set to 0 to skip headers and auto-generate column letter names (A, B, C...) instead.
    /// </summary>
    public static int DataHeaderRow { get; } = 7;

    /// <summary>
    /// Gets the header names for the columns from which to extract rows.
    /// </summary>
    public static string[] DataHeaderNames { get; } = ["PROCESS FAILURE MODE", "RANK", "LOCATION", "DUMMY SAMPLE REQUIRED?"];

    /// <summary>
    /// Gets the first row of actual data (1-based). Rows above this are ignored.
    /// </summary>
    public static int DataStartRow { get; } = 9;

    /// <summary>
    /// Gets the number of consecutive fully-empty rows before stopping sheet parse.
    /// Ensure this value is always greater than the number of consecutive intentional blank rows in the data source.
    /// </summary>
    public static int EmptyRowLimit { get; } = 5;

    /// <summary>
    /// Gets the database name from the environment variable.
    /// </summary>
    public static string DbName => Environment.GetEnvironmentVariable("DB_NAME") ?? "ProductionDB";

    /// <summary>
    /// Gets the connection string for the database whose credentials are stored in environment variables.
    /// </summary>
    /// <returns>A SQL Server connection string for access to the database.</returns>
    public static string GetConnectionString()
    {
        var builder = new StringBuilder
        {
            DataSource = Environment.GetEnvironmentVariable("DB_SERVER"),
            UserID = Environment.GetEnvironmentVariable("DB_USER"),
            Password = Environment.GetEnvironmentVariable("DB_PASS"),
            InitialCatalog = DbName,
            TrustServerCertificate = true,
        };
        return builder.ConnectionString;
    }
}
