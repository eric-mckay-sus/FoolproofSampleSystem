using Microsoft.Data.SqlClient;
using ENV = System.Environment;
using CsvHelper;
using System.Globalization;
using CsvHelper.Configuration;
using System.Text.RegularExpressions;
using System.Data;

namespace ParseCondensedCsv;

public record FoolproofRecord
{
    // These come from the Metadata header
    public string Model { get; set; }
    public byte Revision { get; set; }
    public DateTime IssueDate { get; set; }
    public string? Issuer { get; set; }

    // These come from the CSV Body
    public string? FailureMode { get; set; }
    public string? Rank { get; set; }
    public string? Location { get; set; }
    public short? PartMasterNum { get; set; }
}

public class UploadFoolproofToDb
{
    static async Task Main(string[] args)
    {
        if (args.Length == 0)
            ErrorOut("Usage: dotnet run <file_or_directory_path>");

        string path = args[0];
        List<string> filesToProcess = [];

        if (Directory.Exists(path))
            filesToProcess.AddRange(Directory.GetFiles(path, "*.csv"));
        else if (File.Exists(path))
            filesToProcess.Add(path);
        else
            ErrorOut($"Path '{path}' not found.");

        string? server = ENV.GetEnvironmentVariable("DB_SERVER"),
                user = ENV.GetEnvironmentVariable("DB_USER"),
                password = ENV.GetEnvironmentVariable("DB_PASS"),
                name = ENV.GetEnvironmentVariable("DB_NAME");

        if (new[] { server, user, password, name }.Any(string.IsNullOrWhiteSpace))
            ErrorOut("Missing database environment variables.");

        var builder = new SqlConnectionStringBuilder {
            DataSource = server, UserID = user, Password = password, InitialCatalog = name,
            TrustServerCertificate = true
        };

        foreach (var file in filesToProcess)
        {
            try
            {
                Console.WriteLine($"Processing: {Path.GetFileName(file)}...");
                await ProcessFile(file, builder.ConnectionString);
                Console.WriteLine($"Finished {Path.GetFileName(file)}.");
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"[SKIP] Failed to process {file}: {ex.Message}");
                Console.ResetColor();
            }
        }
    }

    private static async Task ProcessFile(string filepath, string connectionString)
    {
        using var reader = new StreamReader(filepath);

        // Parse metadata Header
        (string? Model, byte Revision, DateTime IssueDate, string? Issuer) = ParseMetadata(reader);

        // Set up CSV parser for the remaining body
        var config = new CsvConfiguration(CultureInfo.InvariantCulture) {
            HasHeaderRecord = true,
            PrepareHeaderForMatch = args => args.Header.ToUpper(),
        };

        using var csv = new CsvReader(reader, config);
        if (!csv.Read()) return;
        csv.ReadHeader();

        // Create a DataTable to hold the transformed data for BulkCopy
        DataTable dt = CreateFoolproofDataTable();

        while (csv.Read())
        {
            DataRow row = dt.NewRow();
            row["model"] = Model;
            row["revision"] = Revision;
            row["issueDate"] = IssueDate;
            row["issuer"] = (object?)Issuer ?? DBNull.Value;

            row["failureMode"] = csv.GetField("PROCESS FAILURE MODE");
            row["rank"] = csv.GetField("RANK");
            row["location"] = csv.GetField("LOCATION");

            // Extract number after '#'
            string dummyRaw = csv.GetField("DUMMY SAMPLE REQUIRED?") ?? "";
            Match match = Regex.Match(dummyRaw, @"#(\d+)");
            row["partMasterNum"] = match.Success ? byte.Parse(match.Groups[1].Value) : DBNull.Value;

            dt.Rows.Add(row);
        }

        // Stream to DB
        using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        using var bulkCopy = new SqlBulkCopy(connection);
        bulkCopy.DestinationTableName = "dbo.FoolproofInfo";

        // Map DataTable columns to SQL Columns
        foreach (DataColumn col in dt.Columns)
            bulkCopy.ColumnMappings.Add(col.ColumnName, col.ColumnName);

        await bulkCopy.WriteToServerAsync(dt);
    }

    private static (string Model, byte Revision, DateTime IssueDate, string Issuer) ParseMetadata(StreamReader reader)
    {
        string model, issuer;
        byte rev;
        DateTime date;

        // Skip the first global metadata comment
        reader.ReadLine();
        // Read header names (Skip)
        reader.ReadLine();
        // Read the actual metadata values
        string? dataLine = reader.ReadLine()?.Replace("#", "").Trim();

        if (string.IsNullOrEmpty(dataLine)) throw new Exception("Invalid Metadata format.");

        string[] parts = dataLine.Split(',');
        // Concatenate base model (parts[0]) and product (parts[1]) from raw line
        model = $"{parts[0].Trim()} {parts[1].Trim()}";
        rev = TranslateRevString(parts[2].Trim());
        date = DateTime.Parse(parts[3].Trim());
        issuer = parts[4].Trim();

        // Skip the end metadata line
        reader.ReadLine();

        return (model, rev, date, issuer);
    }

    /// <summary>
    /// Translates the revision string to the number-only value it refers to
    /// </summary>
    /// <param name="revString">The string to translate</param>
    /// <returns>The revision number</returns>
    private static byte TranslateRevString(string revString)
    {
        revString = revString switch
        {
            "ORIG" => "0",
            "DRAFT" => "0",
            _ => revString.Replace("R", "", StringComparison.OrdinalIgnoreCase)
        };

        return byte.Parse(revString);
    }

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

    private static void ErrorOut(string toPrint)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine(toPrint);
        Console.ResetColor();
        ENV.Exit(-1);
    }
}
