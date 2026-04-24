// <copyright file="Program.cs" company="Stanley Electric US Co. Inc.">
// Copyright (c) 2026 Stanley Electric US Co. Inc. Licensed under the MIT License.
// </copyright>

namespace UploadModelMappings;

using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;
using CsvHelper.Configuration.Attributes;
using Microsoft.Data.SqlClient;

using InterProcessIO;

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
public class ModelMappingUploader
{
    /// <summary>
    /// Determines where user input comes from.
    /// </summary>
    private readonly IInputProvider input;

    /// <summary>
    /// Determines where/how program output is displayed.
    /// </summary>
    private readonly IOutputProvider output;

    /// <summary>
    /// Initializes a new instance of the <see cref="ModelMappingUploader"/> class.
    /// By default, uses the console for input and output.
    /// </summary>
    public ModelMappingUploader()
    {
        this.input = new ConsoleInputProvider();
        this.output = new ConsoleReporter();
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ModelMappingUploader"/> class, using the specified input and output providers.
    /// </summary>
    /// <param name="inputProvider">The instance of IInputProvider to be used to get input regarding model mapping details.</param>
    /// <param name="outputProvider">The instance of IReportOutputProvider to be used for displaying program results.</param>
    public ModelMappingUploader(IInputProvider inputProvider, IOutputProvider outputProvider)
    {
        this.input = inputProvider;
        this.output = outputProvider;
    }

    /// <summary>
    /// Entry point for the program. Parses the entire file for mappings and adds them all to the database.
    /// </summary>
    /// <param name="args">The file to parse (must be a CSV of the correct format).</param>
    /// <returns>A Task representing the completion of this program.</returns>
    public static async Task Main(string[] args)
    {
        // If there was an input location argument, pass it along (no validation here)
        string? potentialFile = null;
        if (args.Length > 0)
        {
            potentialFile = args[0];
        }

        // Exit static by creating an uploader
        ModelMappingUploader uploader = new ();

        // Then give it the green light
        await uploader.ExecuteAsync(potentialFile);
    }

    /// <summary>
    /// Identifies input location, verifies that it is a file, then delegates to the upload handler.
    /// Recommended entry point for other programs which use this one.
    /// </summary>
    /// <param name="filename">An optional file path to override the one found in config.</param>
    /// <returns>A Task representing whether the overwrite was confirmed or canceled.</returns>
    public async Task<UploadResult> ExecuteAsync(string? filename = null)
    {
        string path = Config.GetInputLocation(isFP: false);
        if (string.IsNullOrWhiteSpace(filename))
        {
            await this.Report($"No file specified. Defaulting to config file input location ({path})\n");
        }
        else
        {
            path = filename;
        }

        // Path validation
        try
        {
            if (Directory.Exists(path))
            {
                await this.Report($"Path '{filename}' is a directory, which is not supported by this uploader. Using Config default ({path}).\n", ReportLevel.WARNING);
            }
            else if (!File.Exists(path))
            {
                await this.Report($"Path '{filename}' could not be found. Using Config default ({path}).\n", ReportLevel.WARNING);
            }
            else if (!Path.GetExtension(path).Equals(".csv", StringComparison.OrdinalIgnoreCase))
            {
                await this.Report($"The file you specified ({path}) is not a CSV. Please select a CSV file and try again.", ReportLevel.ERROR);
                return UploadResult.ErroredOut;
            }

            string connectionString = Config.GetConnectionString();

            bool confirmOverwrite = await this.input.GetConfirmAsync(new ($"WARNING: If successful, this action will overwrite the current model info database with the contents of {path}. Proceed?", ReportLevel.WARNING));
            if (!confirmOverwrite)
            {
                return UploadResult.Canceled;
            }

            await this.Upload(path, connectionString);
            return UploadResult.Complete;
        }
        catch (Exception ex)
        {
            await this.Report($"Fatal error: {ex.Message}", ReportLevel.ERROR);
            return UploadResult.ErroredOut;
        }
    }

    /// <summary>
    /// Uploads the CSV file at filepath to the database.
    /// </summary>
    /// <param name="filepath">The path of the CSV to upload.</param>
    /// <param name="connectionString">The DB connection string.</param>
    private async Task Upload(string filepath, string connectionString)
    {
        // The layers of wrapping are kind of disgusting, but we need an open StreamReader to create a CsvReader
        // The CsvReader gives us access to CsvDataReader to stream from the table (to the SqlBulkCopy)
        using StreamReader reader = new (filepath);
        using CsvReader csv = new (reader, CultureInfo.InvariantCulture);
        csv.Context.RegisterClassMap<ModelInfoMap>();
        using CsvDataReader dr = new (csv);

        await this.Report("Connecting...");
        using SqlConnection connection = new (connectionString);
        await connection.OpenAsync();
        using SqlTransaction transaction = connection.BeginTransaction();
        await this.Report("Connected!\n");

        try
        {
            // Now parsing is complete, prepare to completely overwrite old DB state with new
            using (var deleteCommand = new SqlCommand("TRUNCATE TABLE EL2AuthorizedReset.dbo.ModelToLine", connection, transaction))
            {
                deleteCommand.ExecuteNonQuery();
            }

            await this.Report("Uploading...");
            using SqlBulkCopy bulkCopy = new (connection, SqlBulkCopyOptions.Default, transaction);
            bulkCopy.DestinationTableName = "ModelToLine";

            // This looks like RBAR, but it's really just an abstraction
            await bulkCopy.WriteToServerAsync(dr);
            await transaction.CommitAsync();
            await this.Report("Complete!", ReportLevel.SUCCESS);
        }
        catch (Exception ex)
        {
            transaction.Rollback();
            await this.Report($"Bulk Copy Error: {ex.Message}\n", ReportLevel.ERROR);
        }
    }

    /// <summary>
    /// Creates a report and passes it to the output provider.
    /// </summary>
    /// <param name="msg">The message to report.</param>
    /// <param name="level">The message's report level.</param>
    /// <returns>A Task representing that the report has been displayed to the user.</returns>
    private async Task Report(string msg, ReportLevel level = ReportLevel.INFO) => await this.output.ReportAsync(new (msg, level));
}
