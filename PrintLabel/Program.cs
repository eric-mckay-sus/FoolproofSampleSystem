// <copyright file="Program.cs" company="Stanley Electric US Co. Inc.">
// Copyright (c) 2026 Stanley Electric US Co. Inc. Licensed under the MIT License.
// </copyright>

namespace PrintLabel;

using System.Net.Sockets;

using InterProcessIO;

/// <summary>
/// A DTO for the upload/print information required by <see cref="ZebraPrintFlow.ExecuteAsync(ZplCommand, bool)"/>.
/// </summary>
public record ZplCommand
{
    /// <summary>
    /// Gets or sets the ID of the sample to be printed.
    /// </summary>
    public int? SampleId { get; set; }

    /// <summary>
    /// Gets or sets the printer's DPI.
    /// </summary>
    public int PrintDpi { get; set; } = Config.PrinterDpi;
}

/// <summary>
/// Defines the flow of retrieving and transmitting (over TCP) print information (template file, DPI, and sample ID) to a Zebra printer.
/// </summary>
public partial class ZebraPrintFlow
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
    /// Retrieves sample data used to fill label templates.
    /// </summary>
    private readonly ISampleLabelSource sampleSource;

    /// <summary>
    /// Initializes a new instance of the <see cref="ZebraPrintFlow"/> class.
    /// By default, uses the console for I/O, and the DB connection method in <see cref="Config"/> to access sample table.
    /// </summary>
    public ZebraPrintFlow()
    {
        this.input = new ConsoleInputProvider();
        this.output = new ConsoleReporter();
        this.sampleSource = new SqlSampleLabelSource();
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ZebraPrintFlow"/> class, using the specified input and output providers.
    /// By default, uses DB connection method in <see cref="Config"/> to access sample table.
    /// </summary>
    /// <param name="inputProvider">The instance of IInputProvider to be used to get input regarding FP sheet details.</param>
    /// <param name="outputProvider">The instance of IReportOutputProvider to be used for displaying program results.</param>
    public ZebraPrintFlow(IInputProvider inputProvider, IOutputProvider outputProvider)
    {
        this.input = inputProvider;
        this.output = outputProvider;
        this.sampleSource = new SqlSampleLabelSource();
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ZebraPrintFlow"/> class, using the specified input/output providers and sample data source.
    /// </summary>
    /// <param name="inputProvider">The instance of IInputProvider to be used to get input regarding FP sheet details.</param>
    /// <param name="outputProvider">The instance of IReportOutputProvider to be used for displaying program results.</param>
    /// <param name="sampleLabelSource">The source of sample data for label rendering.</param>
    public ZebraPrintFlow(IInputProvider inputProvider, IOutputProvider outputProvider, ISampleLabelSource sampleLabelSource)
    {
        this.input = inputProvider;
        this.output = outputProvider;
        this.sampleSource = sampleLabelSource;
    }

    /// <summary>
    /// Application entry point. Instantiates a <see cref="ZebraPrintFlow"/> object and calls <see cref="PromptAndExecute"/> to escape the static.
    /// </summary>
    /// <returns>A Task representing program completion.</returns>
    public static async Task Main()
    {
        ZebraPrintFlow printObject = new ();
        await printObject.PromptAndExecute();
    }

    /// <summary>
    /// Prompts user for mode, filename(s), and sample to print (all with validation), then delegates to <see cref="ExecuteAsync(ZplCommand, bool)"/> to upload/print.
    /// Call <see cref="ExecuteAsync(ZplCommand, bool)"/> directly if enough data to form a valid <see cref="ZplCommand"/> is on hand.
    /// </summary>
    /// <returns>A Task representing that the arguments have been parsed and executed.</returns>
    public async Task PromptAndExecute()
    {
        ZplCommand zplCmd = new ();

        await this.PromptPrint(zplCmd);

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
    /// Uses manual connection management to avoid closing a connection during a batch.
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
                await zplConn.ConnectAsync(Config.PrinterIp, Config.PrinterPort);
            }

            NetworkStream stream = zplConn.GetStream();

            try
            {
                await this.PrintAsync(zplCmd, stream);
            }
            finally
            {
                if (!leaveOpen)
                {
                    await stream.DisposeAsync();
                }
            }

            return new Report("Print complete", ReportLevel.SUCCESS);
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
