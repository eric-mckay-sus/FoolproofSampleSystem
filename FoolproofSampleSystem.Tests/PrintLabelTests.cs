// <copyright file="PrintLabelTests.cs" company="Stanley Electric US Co. Inc.">
// Copyright (c) 2026 Stanley Electric US Co. Inc. Licensed under the MIT License.
// </copyright>

namespace FoolproofSampleSystem.Tests;

using System.Text;
using InterProcessIO;
using PrintLabel;

public sealed class PrintLabelTests
{
    [Fact]
    public async Task BuildZplCommandAsync_RendersTemplateWithoutOpeningPrinterConnection()
    {
        string templatePath = Path.Combine(Path.GetTempPath(), $"label-{Guid.NewGuid():N}.zpl");
        await File.WriteAllTextAsync(templatePath, "^XA^FD{0}-{1}^FS^XZ");
        ZebraPrintFlow flow = new (new QueueInputProvider(), new CapturingOutputProvider(), new InMemorySampleLabelSource(new Dictionary<int, string[]>()));

        try
        {
            string command = await flow.BuildZplCommand(templatePath, ["sample", "model"]);

            Assert.Equal("^XA^FDsample-model^FS^XZ", command);
        }
        finally
        {
            File.Delete(templatePath);
        }
    }

    [Fact]
    public async Task PrintAsync_WritesToProvidedStreamInsteadOfOpeningPrinterConnection()
    {
        string templatePath = Path.Combine(Path.GetTempPath(), $"label-{Guid.NewGuid():N}.zpl");
        await File.WriteAllTextAsync(templatePath, "^XA^FD{0}:{4}^FS^XZ");
        string originalTemplate = Config.DpiToTemplatePath[203];
        Config.DpiToTemplatePath[203] = templatePath;
        CapturingOutputProvider output = new ();
        string[] fields = ["D12", "MODEL", "A", "LINE", "42", "1", "01/02/2026", "Failure", "Loc", "1001"];
        ZebraPrintFlow flow = new (new QueueInputProvider(), output, new InMemorySampleLabelSource(new Dictionary<int, string[]> { [42] = fields }));
        await using MemoryStream destination = new ();

        try
        {
            await flow.PrintAsync(new ZplCommand { SampleId = 42, PrintDpi = 203 }, destination);

            Assert.Equal("^XA^FDD12:42^FS^XZ", Encoding.UTF8.GetString(destination.ToArray()));
            Assert.Contains(output.Reports, report => report.level == ReportLevel.SUCCESS);
        }
        finally
        {
            Config.DpiToTemplatePath[203] = originalTemplate;
            File.Delete(templatePath);
        }
    }

    [Fact]
    public async Task PrintAsync_InvalidDpiReportsAndDoesNotWrite()
    {
        CapturingOutputProvider output = new ();
        ZebraPrintFlow flow = new (new QueueInputProvider(), output, new InMemorySampleLabelSource(new Dictionary<int, string[]>()));
        await using MemoryStream destination = new ();

        await flow.PrintAsync(new ZplCommand { SampleId = 42, PrintDpi = 999 }, destination);

        Assert.Empty(destination.ToArray());
        Assert.Contains(output.Reports, report => report.message.Contains("no configured option", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task PromptPrint_RejectsMissingSampleBeforeWritingCommand()
    {
        QueueInputProvider input = new (["not-an-id", "42", string.Empty]);
        CapturingOutputProvider output = new ();
        ZebraPrintFlow flow = new (input, output, new InMemorySampleLabelSource(new Dictionary<int, string[]> { [42] = ["D12"] }));
        ZplCommand command = new ();

        await flow.PromptPrint(command);

        Assert.Equal(42, command.SampleId);
        Assert.Equal(Config.PrinterDpi, command.PrintDpi);
        Assert.Equal(3, input.InputRequests.Count);
    }

    [Fact]
    public async Task PromptPrint_RejectsUnknownSampleId()
    {
        QueueInputProvider input = new (["99", "42", string.Empty]);
        ZebraPrintFlow flow = new (input, new CapturingOutputProvider(), new InMemorySampleLabelSource(new Dictionary<int, string[]> { [42] = ["D12"] }));
        ZplCommand command = new ();

        await flow.PromptPrint(command);

        Assert.Equal(42, command.SampleId);
        Assert.Equal(3, input.InputRequests.Count);
    }
}
