// <copyright file="DbUploadUtilities.cs" company="Stanley Electric US Co. Inc.">
// Copyright (c) 2026 Stanley Electric US Co. Inc. Licensed under the MIT License.
// </copyright>

namespace UploadFpInfo;

using Microsoft.Data.SqlClient;
using System.Data;

using InterProcessIO;

/// <summary>
/// Contains methods useful for uploading foolproof data sheets to the foolproof info database.
/// Independent of file reading.
/// </summary>
public static class DbUploadUtilities
{
    // These column names are used frequently enough to merit separate storage
    private static readonly string RevisionColName = "revision";
    private static readonly string LocationColName = "location";
    private static readonly string DummySampleColName = "dummySampleNum";

    /// <summary>
    /// Verifies that a particular model exists in the model to line (MTL) database.
    /// </summary>
    /// <param name="toValidate">The model name to validate.</param>
    /// <returns>The string <paramref name="toValidate"/> as it appears in the MTL database, otherwise null.</returns>
    public static async Task<string?> ValidateModel(string? toValidate)
    {
        if (string.IsNullOrWhiteSpace(toValidate))
        {
            return null;
        }

        using SqlConnection conn = new (Config.GetConnectionString());
        await conn.OpenAsync();

        string sql = @"
            SELECT TOP 1 shortDesc FROM dbo.ModelToLine
                   WHERE shortDesc = @model";

        using SqlCommand cmd = new (sql, conn);
        cmd.Parameters.AddWithValue("@model", toValidate);

        string? actual = (string?)await cmd.ExecuteScalarAsync();

        return actual;
    }

    /// <summary>
    /// Attempts to upload all contents of <paramref name="dt"/> over <paramref name="conn"/>.
    /// First attempts a standard SqlBulkCopy for speed, but if that fails, falls back to row-by row for granularity.
    /// </summary>
    /// <param name="dt">The DataTable to upload.</param>
    /// <param name="conn">The SqlConnection used to connect to the database.</param>
    /// <returns>A Task containing the <see cref="ParseResult"/> signifying the success/failure of the upload, and a stack containing error reports.</returns>
    public static async Task<(ParseResult, Stack<Report>)> AttemptUpload(DataTable dt, SqlConnection conn)
    {
        try
        {
            // Attempt a bulk copy
            await ExecuteBulkCopy(dt, conn);
            return default;
        }
        catch (Exception)
        {
            // If bulk copy fails, fall back to row-by-row to find the culprit
            Stack<Report> reportStack = new (); // Use a stack to ensure the skips are printed in the order they appear in the file
            ParseResult parseResult = default;

            // Iterate in reverse to guarantee indices don't move on deletion
            for (int i = dt.Rows.Count - 1; i >= 0; i--)
            {
                DataRow dr = dt.Rows[i];

                (ParseResult rowResult, Report? skipReport) = await TryWriteRow(dr, conn);
                parseResult |= rowResult;

                if (skipReport != null)
                {
                    reportStack.Push(skipReport);
                    dt.Rows.RemoveAt(i); // remove the problem row
                }
            }

            reportStack.Push(new ("\t[BULK FAILED] One or more entries in this file could not be added to the database. Switching insertion modes for error reporting...\n", ReportLevel.WARNING));

            // If there are no more rows in the original DataTable, every one was a duplicate.
            if (dt.Rows.Count == 0)
            {
                parseResult |= new ParseResult(alreadyUploaded: true);
            }

            return (parseResult, reportStack);
        }
    }

    /// <summary>
    /// Copies the contents of <paramref name="dt"/> to the foolproof info table over <paramref name="conn"/>.
    /// </summary>
    /// <param name="dt">The DataTable to be uploaded to the foolproof info table.</param>
    /// <param name="conn">The connection to use to access the database.</param>
    /// <returns>A Task representing that the upload is complete.</returns>
    public static async Task ExecuteBulkCopy(DataTable dt, SqlConnection conn)
    {
        using SqlBulkCopy bulkCopy = new (conn);
        bulkCopy.DestinationTableName = "dbo.FoolproofInfo";

        // Force SqlBulkCopy to respect column NAMES, not POSITIONS
        bulkCopy.ColumnMappings.Add("model", "model");
        bulkCopy.ColumnMappings.Add(RevisionColName, RevisionColName);
        bulkCopy.ColumnMappings.Add("issueDate", "issueDate");
        bulkCopy.ColumnMappings.Add("issuer", "issuer");
        bulkCopy.ColumnMappings.Add("failureMode", "failureMode");
        bulkCopy.ColumnMappings.Add("rank", "rank");
        bulkCopy.ColumnMappings.Add(LocationColName, LocationColName);
        bulkCopy.ColumnMappings.Add(DummySampleColName, DummySampleColName);

        await bulkCopy.WriteToServerAsync(dt);
    }

    /// <summary>
    /// Wrapper for <see cref="WriteRowToDatabase"/> that provides a status update.
    /// </summary>
    /// <param name="dr">The DataRow to write.</param>
    /// <param name="conn">The SqlConnection to use for the row write attempt.</param>
    /// <returns>A <see cref="ParseResult"/> representing this particular row's errors, and a <see cref="Report"/> containing the message to display (in case of error).</returns>
    public static async Task<(ParseResult rowResult, Report? skipReport)> TryWriteRow(DataRow dr, SqlConnection conn)
    {
        try
        {
            await WriteRowToDatabase(dr, conn);
            return default;
        }
        catch (SqlException rowEx) when (rowEx.Number == 2627 || rowEx.Number == 2601)
        {
            return (new ParseResult(hasDuplicate: true), new ($"\t[ROW SKIP] Duplicate: Rev {dr[RevisionColName]}, Location {dr[LocationColName]} Dummy #{dr[DummySampleColName]}\n", ReportLevel.WARNING));
        }
        catch (Exception rowEx)
        {
            return (new ParseResult(hasDuplicate: true), new ($"\t[ROW SKIP] Error: {rowEx.Message}\n", ReportLevel.ERROR));
        }
    }

    /// <summary>
    /// Asynchronously writes the input DataRow's contents to the FP info table.
    /// Only use this method after attempting (and failing) a bulk copy.
    /// </summary>
    /// <param name="dr">The DataRow whose contents will be written to the server.</param>
    /// <param name="conn">The open SQL connection to be used in the SqlCommand.</param>
    /// <returns>A Task representing the completion (or failure) of the insertion.</returns>
    private static async Task WriteRowToDatabase(DataRow dr, SqlConnection conn)
    {
        string sql = @"
            INSERT INTO dbo.FoolproofInfo
            (model, revision, issueDate, issuer, failureMode, rank, location, dummySampleNum)
            VALUES
            (@model, @revision, @issueDate, @issuer, @failureMode, @rank, @location, @dummySampleNum)";

        using SqlCommand cmd = new (sql, conn);

        // Mapping parameters from the DataRow
        cmd.Parameters.Add("@model", SqlDbType.VarChar, 32).Value = dr["model"];
        cmd.Parameters.Add("@revision", SqlDbType.TinyInt).Value = dr[RevisionColName];
        cmd.Parameters.Add("@issueDate", SqlDbType.Date).Value = dr["issueDate"];
        cmd.Parameters.Add("@issuer", SqlDbType.VarChar, 32).Value = dr["issuer"];
        cmd.Parameters.Add("@failureMode", SqlDbType.VarChar, 100).Value = dr["failureMode"];
        cmd.Parameters.Add("@rank", SqlDbType.Char, 1).Value = dr["rank"];
        cmd.Parameters.Add("@location", SqlDbType.VarChar, 100).Value = dr[LocationColName];
        cmd.Parameters.Add("@dummySampleNum", SqlDbType.SmallInt).Value = dr[DummySampleColName];

        await cmd.ExecuteNonQueryAsync();
    }
}
