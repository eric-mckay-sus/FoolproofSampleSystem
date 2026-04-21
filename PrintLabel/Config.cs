// <copyright file="Config.cs" company="Stanley Electric US Co. Inc.">
// Copyright (c) 2026 Stanley Electric US Co. Inc. Licensed under the MIT License.
// </copyright>

namespace PrintLabel;

using StringBuilder = Microsoft.Data.SqlClient.SqlConnectionStringBuilder;

/// <summary>
/// A container for the data that is constant in UploadFpInfo (but could change).
/// </summary>
internal static class Config
{
    /// <summary>
    /// Gets or sets the program-side path to the template file to upload.
    /// </summary>
    public static string UploadPath { get; set; } = @"C:\LOCAL PROGRAMS\FoolproofSampleSystem\PrintLabel\FpSample204.zpl";

    /// <summary>
    /// Gets or sets the printer-side path to the template file to load and print.
    /// </summary>
    public static string PrintPath { get; set; } = @"R:\FPSAMPLE203.ZPL";

    /// <summary>
    /// Gets the safe size limit for a ZPL file (RAM precaution).
    /// </summary>
    public static int KbLimit { get; } = 20;

    /// <summary>
    /// Gets the connection string for the database whose credentials are stored in environment variables.
    /// </summary>
    /// <returns>A SQL Server connection string for access to the database.</returns>
    /// <throws>InvalidOperationException when there are missing environment variable(s).</throws>
    public static string GetConnectionString()
    {
        StringBuilder builder = new ()
        {
            DataSource = GetRequired("DB_SERVER"),
            UserID = GetRequired("DB_USER"),
            Password = GetRequired("DB_PASS"),
            InitialCatalog = GetRequired("DB_NAME"),
            TrustServerCertificate = true,
        };
        return builder.ConnectionString;
    }

    /// <summary>
    /// Wrapper for <see cref="GetRequired"/> to get the printer IP address.
    /// </summary>
    /// <returns>A string of the target printer IP address.</returns>
    public static string GetPrinterIp()
    {
        return GetRequired("ZEBRA_PRINTER_IP");
    }

    /// <summary>
    /// Gets a required value from the environment.
    /// </summary>
    /// <param name="key">The key to get the environment variable.</param>
    /// <returns>The value associated with the key, or <see cref="InvalidOperationException"/> if not found.</returns>
    /// <exception cref="InvalidOperationException">Thrown when <paramref name="key"/> cannot be found in the environment.</exception>
    private static string GetRequired(string key)
    {
        string? value = Environment.GetEnvironmentVariable(key);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Required environment variable '{key}' is missing for {(key.Contains("DB") ? "database" : "printer")} connection.");
        }

        return value;
    }
}
