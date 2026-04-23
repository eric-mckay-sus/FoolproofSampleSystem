// <copyright file="Program.cs" company="Stanley Electric US Co. Inc.">
// Copyright (c) 2026 Stanley Electric US Co. Inc. Licensed under the MIT License.
// </copyright>

namespace PrintLabel;

using System.Net.Sockets;

using InterProcessIO;

/// <summary>
/// A DTO for the upload/print information required by <see cref="ZebraUploadPrint.ExecuteAsync(ZplCommand, bool)"/>.
/// </summary>
public record ZplCommand
{
    /// <summary>
    /// Gets or sets a value indicating whether upload mode is engaged.
    /// </summary>
    public bool IsUpload { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether print mode is engaged.
    /// </summary>
    public bool IsPrint { get; set; }

    /// <summary>
    /// Gets or sets print mode's sample ID.
    /// </summary>
    public int? SampleId { get; set; } = null;

    /// <summary>
    /// Gets or sets the path on this machine of the ZPL file to be uploaded.
    /// Always check <see cref="IsUpload"/> before accessing to verify validity.
    /// </summary>
    public string UploadPath { get; set; } = Config.UploadPath;

    /// <summary>
    /// Gets or sets the path on the printer of the ZPL file to be printed.
    /// Always check <see cref="IsPrint"/> before accessing to verify validity.
    /// </summary>
    public string PrintPath { get; set; } = Config.PrintPath;
}

/// <summary>
/// Connects to a Zebra printer over TCP to upload a template or print a sample by ID.
/// </summary>
public partial class ZebraUploadPrint
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
    /// Initializes a new instance of the <see cref="ZebraUploadPrint"/> class.
    /// By default, uses the console for input and output.
    /// </summary>
    public ZebraUploadPrint()
    {
        this.input = new ConsoleInputProvider();
        this.output = new ConsoleReporter();
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ZebraUploadPrint"/> class, using the specified input and output providers.
    /// </summary>
    /// <param name="inputProvider">The instance of IInputProvider to be used to get input regarding FP sheet details.</param>
    /// <param name="outputProvider">The instance of IReportOutputProvider to be used for displaying program results.</param>
    public ZebraUploadPrint(IInputProvider inputProvider, IOutputProvider outputProvider)
    {
        this.input = inputProvider;
        this.output = outputProvider;
    }

    /// <summary>
    /// Application entry point. Instantiates a <see cref="ZebraUploadPrint"/> object and calls <see cref="PromptAndExecute"/> to escape the static.
    /// </summary>
    /// <returns>A Task representing program completion.</returns>
    public static async Task Main()
    {
        ZebraUploadPrint printObject = new ();
        await printObject.PromptAndExecute();
    }

    /// <summary>
    /// Prompts user for mode, filename(s), and sample to print (all with validation), then delegates to <see cref="ExecuteAsync(ZplCommand, bool)"/> to upload/print.
    /// Call <see cref="ExecuteAsync(ZplCommand, bool)"/> directly if enough data to form a valid <see cref="ZplCommand"/> is on hand.
    /// </summary>
    /// <returns>A Task representing that the arguments have been parsed and executed.</returns>
    public async Task PromptAndExecute()
    {
        ZplCommand zplCmd = new ()
        {
            IsUpload = await this.input.GetConfirmAsync(new ("Do you wish to upload a new template?")),
        };

        if (zplCmd.IsUpload)
        {
            await this.PromptUpload(zplCmd);
        }

        zplCmd.IsPrint = await this.input.GetConfirmAsync(new ($"Do you wish to {(zplCmd.IsUpload ? "print a sample using the new template" : "print a sample using a template already on this printer")}?"));

        if (zplCmd.IsPrint)
        {
            await this.PromptPrint(zplCmd);
        }

        // Use the default TCP connection
        await this.ExecuteAsync(zplCmd);
    }

    /// <summary>
    /// Overload for <see cref="ExecuteAsync(ZplCommand, TcpClient, bool)"/> that defaults to a TCP connection to the config file IP address at the default port.
    /// </summary>
    /// <param name="zplCmd">The arguments to pass into <see cref="ExecuteAsync(ZplCommand, TcpClient, bool)"/>.</param>
    /// <param name="leaveOpen">Whether to leave the connection open for future use (e.g. batching).</param>
    /// <returns> A <see cref="Report"/> with the upload/print status.</returns>
    public async Task<Report> ExecuteAsync(ZplCommand zplCmd, bool leaveOpen = false)
    {
        return await this.ExecuteAsync(zplCmd, new TcpClient(), leaveOpen);
    }

    /// <summary>
    /// Uploads/prints to the ZPL printer connected via <paramref name="zplConn"/> according to the instructions in <paramref name="zplCmd"/>.
    /// </summary>
    /// <param name="zplCmd">The <see cref="ZplCommand"/> containing upload/print information.</param>
    /// <param name="zplConn">The <see cref="TcpClient"/> representing the printer connection.</param>
    /// <param name="leaveOpen">Whether to leave the connection open for future use (e.g. batching).</param>
    /// <returns>A <see cref="Report"/> with the upload/print status.</returns>
    public async Task<Report> ExecuteAsync(ZplCommand zplCmd, TcpClient zplConn, bool leaveOpen = false)
    {
        try
        {
            // If the client wasn't already connected to the printer, connect them now
            if (!zplConn.Connected)
            {
                await zplConn.ConnectAsync(Config.GetPrinterIp(), Config.PrinterPort);
            }

            using NetworkStream stream = zplConn.GetStream();

            List<string> completedList = [];

            if (zplCmd.IsUpload)
            {
                await this.UploadAsync(zplCmd, stream);
                completedList.Add("Upload");
            }

            if (zplCmd.IsPrint)
            {
                await this.PrintAsync(zplCmd, stream);
                completedList.Add(zplCmd.IsUpload ? "print" : "Print");
            }

            string completedOps = string.Join(" and ", completedList);
            return new Report($"{completedOps} complete", ReportLevel.SUCCESS);
        }
        catch (SocketException e)
        {
            Report error = new ($"Error connecting to printer: {e.Message}", ReportLevel.ERROR);
            await this.output.ReportAsync(error);
            return error;
        }
        catch (IOException e)
        {
            Report error = new ($"Error executing the print command: {e.Message}", ReportLevel.ERROR);
            await this.output.ReportAsync(error);
            return error;
        }
        finally
        {
            // In case the connection opening caused the exception
            if (!leaveOpen && zplConn.Connected)
            {
                zplConn.Close();
            }
        }
    }

    /// <summary>
    /// Creates a report and passes it to the output provider.
    /// Enclose console-specific information in parentheses for Blazor to hide it.
    /// </summary>
    /// <param name="msg">The message to report.</param>
    /// <param name="level">The message's report level.</param>
    /// <returns>A Task representing that the report has been displayed to the user.</returns>
    private async Task Report(string msg, ReportLevel level = ReportLevel.INFO) => await this.output.ReportAsync(new (msg, level));
}
