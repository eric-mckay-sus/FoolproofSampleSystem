// <copyright file="Program.cs" company="Stanley Electric US Co. Inc.">
// Copyright (c) 2026 Stanley Electric US Co. Inc. Licensed under the MIT License.
// </copyright>

namespace UploadModelMappings;

using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;
using CsvHelper.Configuration.Attributes;
using Microsoft.Data.SqlClient;
using ENV = Environment;

/// <summary>
/// A mappable DTO to contain the information associated with a model
/// </summary>
public record ModelInfo
{
    /// <summary>
    /// Gets or sets the ICS number of this model row.
    /// </summary>
    [Name("INTERNAL_PART_#")]
    public required string IcsNum { get; set; }

    /// <summary>
    /// Gets or sets the ShortDescription field of this model row.
    /// </summary>
    [Name("SHORT_DESC")]
    public required string ShortDescription { get; set; }

    /// <summary>
    /// Gets or sets the production cell code of this model row.
    /// </summary>
    [Name("PROD_CELL_CODE")]
    public required string ProdCellCode { get; set; }

    /// <summary>
    /// Gets or sets the work center code of this model row.
    /// </summary>
    [Name("WORK_CENTER_CODE")]
    public required string WorkCenterCode { get; set; }

    /// <summary>
    /// Gets or sets the full description field of this model row.
    /// </summary>
    [Name("DESCRIPTION")]
    public required string Description { get; set; }
}

/// <summary>
/// A ClassMap for ModelInfo that maps the column names of an input CSV to their corresponding fields.
/// </summary>
public sealed class ModelInfoMap : ClassMap<ModelInfo>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ModelInfoMap"/> class.
    /// </summary>
    public ModelInfoMap()
    {
        this.Map(m => m.IcsNum).Name("INTERNAL_PART_#");
        this.Map(m => m.ShortDescription).Name("SHORT_DESC");
        this.Map(m => m.ProdCellCode).Name("PROD_CELL_CODE");
        this.Map(m => m.WorkCenterCode).Name("WORK_CENTER_CODE");
        this.Map(m => m.Description).Name("DESCRIPTION");
    }
}

/// <summary>
/// A CSV parser to get the current models and lines on which they are run.
/// Upon successful parsing, replaces the current dataset in the DB.
/// </summary>
public class UploadModelsToDb
{
    /// <summary>
    /// Entry point for the program. Parses the entire file for mappings and adds them all to the database.
    /// </summary>
    /// <param name="args">The file to parse (must be a CSV of the correct format).</param>
    /// <returns>A Task representing the completion of this program.</returns>
    public static async Task Main(string[] args)
    {
        if (args.Length == 0)
        {
            ErrorOut("No file argument detected. Please retry and supply path for file from which to harvest line mappings.");
        }

        string file = args[0];
        if (!File.Exists(file))
        {
            ErrorOut($"The file you specified ({file}) could not be found. Please check your spelling and try again. The path may be relative to this program or absolute.");
        }

        if (!Path.GetExtension(file).Equals(".csv", StringComparison.OrdinalIgnoreCase))
        {
            ErrorOut($"The file you specified ({file}) is not a CSV. Please select a CSV file and try again.");
        }

        string? server = ENV.GetEnvironmentVariable("DB_SERVER"), user = ENV.GetEnvironmentVariable("DB_USER"), password = ENV.GetEnvironmentVariable("DB_PASS"), name = ENV.GetEnvironmentVariable("DB_NAME");

        if (string.IsNullOrWhiteSpace(server) || string.IsNullOrWhiteSpace(user) || string.IsNullOrWhiteSpace(password) || string.IsNullOrWhiteSpace(name))
        {
            ErrorOut("One or more environment variables for database connection are missing. Please reload your terminal (or its context) and try again.");
        }

        var builder = new SqlConnectionStringBuilder
        {
            DataSource = server,
            UserID = user,
            Password = password,
            InitialCatalog = name,
            TrustServerCertificate = true, // TODO insecure, eventually require certificate verification
        };

        Console.WriteLine($"WARNING: If successful, this action will overwrite the current model info database with the contents of {file}. Proceed? (y/n)");

        string userConfirmation = Console.ReadLine() ?? string.Empty;

        if (!userConfirmation.Equals("y", StringComparison.OrdinalIgnoreCase))
        {
            return; // default to cancel if user does not input y or Y
        }

        var consoleProgress = new Progress<string>(msg => Console.Write(msg));
        await Upload(file, builder.ConnectionString, consoleProgress);
    }

    /// <summary>
    /// Prints the specified string to standard output in red, then exits the program.
    /// </summary>
    /// <param name="toPrint">The string to print.</param>
    private static void ErrorOut(string toPrint)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine(toPrint);
        Console.ResetColor();
        ENV.Exit(-1);
    }

    /// <summary>
    /// Uploads the CSV file at filepath to the database.
    /// </summary>
    /// <param name="filepath">The path of the CSV to upload.</param>
    /// <param name="connectionString">The DB conneciton string.</param>
    /// <param name="progress">The IProgress implementation to which the progress should be reported.</param>
    private static async Task Upload(string filepath, string connectionString, IProgress<string>? progress = null)
    {
        // Called as a conditional Console.Write
        void Report(string msg) => progress?.Report(msg);

        if (!Path.GetExtension(filepath).Equals(".csv", StringComparison.OrdinalIgnoreCase))
        {
            Report("The file you provided is not a CSV. Please ensure the input file is of the correct filetype and format, then try again");
            return;
        }

        // The layers of wrapping are kind of disgusting, but we need an open StreamReader to create a CsvReader
        // The CsvReader gives us access to CsvDataReader to stream from the table (to the SqlBulkCopy)
        using StreamReader reader = new (filepath);
        using CsvReader csv = new (reader, CultureInfo.InvariantCulture);
        csv.Context.RegisterClassMap<ModelInfoMap>();
        using CsvDataReader dr = new (csv);

        Report("Connecting...");
        using SqlConnection connection = new (connectionString);
        await connection.OpenAsync();
        using SqlTransaction transaction = connection.BeginTransaction();
        Report("Connected!\n");

        try
        {
            // Now parsing is complete, prepare to completely overwrite old DB state with new
            using (var deleteCommand = new SqlCommand("TRUNCATE TABLE EL2AuthorizedReset.dbo.ModelToLine", connection, transaction))
            {
                deleteCommand.ExecuteNonQuery();
            }

            Report("Uploading...");
            using SqlBulkCopy bulkCopy = new (connection, SqlBulkCopyOptions.Default, transaction);
            bulkCopy.DestinationTableName = "ModelToLine";
            await bulkCopy.WriteToServerAsync(dr);
            await transaction.CommitAsync();
            Report("Complete!");
        }
        catch (Exception ex)
        {
            transaction.Rollback();
            Report($"Bulk Copy Error: {ex.Message}\n");
            throw; // so the caller knows it failed
        }
    }
}
