// <copyright file="Program.cs" company="Stanley Electric US Co. Inc.">
// Copyright (c) 2026 Stanley Electric US Co. Inc. Licensed under the MIT License.
// </copyright>

namespace PrintLabel;

using System.Text;
using Zebra.Sdk.Comm;
using Microsoft.Data.SqlClient;

using PrintCommon;

/// <summary>
/// Utilizes the Zebra printer SDK to connect to a ZPL printer and upload a template or print a sample by ID.
/// </summary>
public class ZebraUploadPrint : IPrintService
{
    /// <summary>
    /// Application entry point. Parses command-line input for mode, filename, and sample to print, then delegates to <see cref="ExecuteAsync(ZplCommand)"/> to upload/print.
    /// </summary>
    /// <param name="args">The command line arguments specifying whether to upload, print, or both (and which file/sample ID).</param>
    /// <returns>A Task representing program completion.</returns>
    public static async Task Main(string[] args)
    {
        // The program requires at least one argument (mode keyword)
        if (args.Length == 0)
        {
            Console.WriteLine("Usage: dotnet run <UPLOAD|PRINT|UPLOAD-PRINT> [sample ID] [computer_path.zpl] [printer_path.zpl]");
            return;
        }

        // Immediately identify and verify the first argument (must be upload or print)
        ZplCommand parsed = new ();
        string modeArg = args[0].ToUpper();
        parsed.IsUpload = modeArg.Contains("UPLOAD");
        parsed.IsPrint = modeArg.Contains("PRINT");

        // If the first argument doesn't specify upload/print, cut it off early
        if (!(parsed.IsUpload || parsed.IsPrint))
        {
            Console.WriteLine("Usage: dotnet run <UPLOAD|PRINT|UPLOAD-PRINT> [sample ID] [computer_path.zpl] [printer_path.zpl]");
            return;
        }

        // The mode keyword is accounted for now, so we skip it. We build a queue to process further argument
        Queue<string> argQueue = new (args.Skip(1));

        // By only popping the next argument if in print mode, we guarantee the last one is the filename (if present)
        if (parsed.IsPrint)
        {
            // Assume that if the second argument is a path on this machine, the user intended it to be the file to upload/print
            if (!argQueue.TryDequeue(out string? idString) || Path.Exists(idString))
            {
                Console.WriteLine("PRINT mode requires a sample ID argument.");
                Console.WriteLine($"Usage: dotnet run <UPLOAD|PRINT|UPLOAD-PRINT> <sample ID> {(parsed.IsPrint ? "[computer_path.zpl]" : string.Empty)} [printer_path.zpl]");
                return;
            }

            if (!int.TryParse(idString, out int sampleId))
            {
                Console.WriteLine($"Sample ID '{idString}' is not an integer. Please try again.");
                return;
            }

            parsed.SampleId = sampleId;
        }

        // If in upload mode, the next argument is the upload filename. If not in upload mode, leave the upload path as null.
        if (parsed.IsUpload)
        {
            parsed.UploadPath = Config.UploadPath;
            if (argQueue.TryDequeue(out string? uploadPath))
            {
                if (File.Exists(uploadPath))
                {
                    parsed.UploadPath = uploadPath;
                }
                else
                {
                    Console.WriteLine($"File '{uploadPath}' not found. Using config file default: {Config.UploadPath}");
                }
            }
            else
            {
                Console.WriteLine($"No upload file specified. Using config file default: {Config.UploadPath}");
            }
        }

        // If in print mode, the next argument is the print filename. If not in print mode, leave the print path as null.
        if (parsed.IsPrint)
        {
            parsed.PrintPath = Config.PrintPath;
            if (argQueue.TryDequeue(out string? printPath))
            {
                // Can't check file existence here bc it's a file on the printer, so we just check that it looks like a good path
                if (printPath.StartsWith("R:") || printPath.StartsWith("E:"))
                {
                    parsed.PrintPath = printPath;
                }
                else
                {
                    Console.WriteLine($"File '{printPath}' must be on the R or E drive. Using config file default: {Config.UploadPath}");
                }
            }
            else
            {
                Console.WriteLine($"No print file specified. Using config file default: {Config.PrintPath}");
            }
        }

        ZebraUploadPrint printObject = new ();

        // Use the default TCP connection
        await printObject.ExecuteAsync(parsed);
    }

    /// <summary>
    /// Overload for <see cref="ExecuteAsync(ZplCommand, Connection)"/> that defaults to a TCP connection to the config file IP address at the default port.
    /// </summary>
    /// <param name="args">The arguments to pass into <see cref="ExecuteAsync(ZplCommand, Connection)"/>.</param>
    /// <returns> A Task representing that the upload/print is complete.</returns>
    public async Task ExecuteAsync(ZplCommand args)
    {
        // Establish a connection with the printer regardless of command
        TcpConnection zplConn = new (Config.GetPrinterIp(), TcpConnection.DEFAULT_ZPL_TCP_PORT);

        await this.ExecuteAsync(args, zplConn);

        // Close connection, if not handled by callee
        if (zplConn.Connected)
        {
            zplConn.Close();
        }
    }

    /// <summary>
    /// Uploads/prints to the ZPL printer connected via <paramref name="zplConn"/> according to the instructions in <paramref name="args"/>.
    /// </summary>
    /// <param name="args">The path to be uploaded to the printer's internal memory. Set to null for print-only.</param>
    /// <param name="zplConn">The path to be printed from the printer's internal memory. Set to null for upload-only.</param>
    /// <returns>A Task representing that the upload/print is complete.</returns>
    public async Task ExecuteAsync(ZplCommand args, Connection zplConn)
    {
        try
        {
            if (!zplConn.Connected)
            {
                zplConn.Open();
            }

            string? printPathShortcut = null;

            if (args.IsUpload)
            {
                // Simple ZPL files are only ever a handful of kilobytes, so verify length, then grab it all for upload without memory concerns.
                FileInfo fileInfo = new (args.UploadPath);
                int kbSize = Convert.ToInt32(fileInfo.Length / 1024);

                if (kbSize > Config.KbLimit)
                {
                    Console.WriteLine($"{args.UploadPath} exceeds the size limit of {Config.KbLimit}KB. Canceling upload...");
                    return;
                }

                string toUpload = File.ReadAllText(args.UploadPath);

                // If the ZPL doesn't contain a DF command (to switch the printer to download mode), don't send it over.
                if (!toUpload.Contains("^DF"))
                {
                    Console.WriteLine($"{args.UploadPath} does not have a download command and would print immediately. Canceling upload...");
                    return;
                }

                // Get the print path "shortcut" (for an upload-print) from the template file itself
                if (args.IsPrint)
                {
                    string startMarker = "^DF";
                    string endMarker = "^FS";

                    int pFrom = toUpload.IndexOf(startMarker) + startMarker.Length;
                    int pTo = toUpload.IndexOf(endMarker, pFrom);

                    printPathShortcut = toUpload[pFrom..pTo];

                    Console.WriteLine($"Extracted file name for print: {printPathShortcut}");
                }

                // Send template to printer memory (execute the download command printer-side)
                zplConn.Write(Encoding.UTF8.GetBytes(toUpload));
                Console.WriteLine("Upload successful!");
            }

            if (args.IsPrint)
            {
                Dictionary<int, string> fields;

                using (SqlConnection sqlConn = new (Config.GetConnectionString()))
                {
                    // Map ^FN numbers to values
                    fields = await SampleMapFromId(args.SampleId, sqlConn);
                }

                // SampleMapFromId only returns empty when the sample ID couldn't be found
                if (fields.Count == 0)
                {
                    Console.WriteLine($"{args.SampleId} is not the ID of a sample in the database. Please try again.");
                    return;
                }

                StringBuilder sb = new ();

                // Recall and print
                sb.Append($"^XA^XF{printPathShortcut ?? args.PrintPath}");
                foreach (KeyValuePair<int, string> entry in fields)
                {
                    sb.Append($"^FN{entry.Key}^FD{entry.Value}^FS");
                }

                sb.Append("^XZ");

                zplConn.Write(Encoding.UTF8.GetBytes(sb.ToString()));
                Console.WriteLine("Sent print command to printer. Print should begin shortly.");
            }
        }
        catch (ConnectionException e)
        {
            Console.WriteLine($"Printer Error: {e.Message}");
        }
        finally
        {
            // In case the connection opening caused the exception
            if (zplConn.Connected)
            {
                zplConn.Close();
            }
        }
    }

    /// <summary>
    /// Queries the sample table by target ID and collects the info necessary to fill out a sample label.
    /// </summary>
    /// <param name="id">The sample serial number.</param>
    /// <param name="conn">The connection to the SQL database.</param>
    /// <returns>A dictionary mapping field numbers (for the ZPL template) to field data (from the database).</returns>
    private static async Task<Dictionary<int, string>> SampleMapFromId(int? id, SqlConnection conn)
    {
        if (id == null)
        {
            return [];
        }

        if (conn.State != System.Data.ConnectionState.Open)
        {
            await conn.OpenAsync();
        }

        var fieldMap = new Dictionary<int, string>();

        // Define the query to pull fields required by the ZPL template
        string query = @"
            SELECT
                dummySampleNum, model, rank,
                workCenterCode, iteration, creationDate,
                failureMode, location, creatorName
            FROM Samples
            WHERE sampleID = @id";

        using (SqlCommand cmd = new (query, conn))
        {
            cmd.Parameters.AddWithValue("@id", id);

            using (SqlDataReader reader = await cmd.ExecuteReaderAsync())
            {
                if (await reader.ReadAsync())
                {
                    // Helper to format strings with the ZPL centering suffix
                    static string Format(object value) => $"{value?.ToString() ?? string.Empty}\\&";

                    // Map database columns to ZPL ^FN indices
                    fieldMap.Add(1,  Format(reader["dummySampleNum"]));
                    fieldMap.Add(2,  Format(reader["model"]));
                    fieldMap.Add(3,  Format(reader["rank"]));
                    fieldMap.Add(4,  Format(id));
                    fieldMap.Add(5,  Format(reader["workCenterCode"]));
                    fieldMap.Add(6,  Format(reader["iteration"]));
                    fieldMap.Add(7,  Format(((DateTime)reader["creationDate"]).ToString("MM/dd/yyyy")));
                    fieldMap.Add(8,  Format(reader["failureMode"]));
                    fieldMap.Add(9,  Format(reader["location"]));
                    fieldMap.Add(10, Format(reader["creatorName"]));
                }
            }
        }

        return fieldMap;
    }
}
