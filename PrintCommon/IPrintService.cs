// <copyright file="IPrintService.cs" company="Stanley Electric US Co. Inc.">
// Copyright (c) 2026 Stanley Electric US Co. Inc. Licensed under the MIT License.
// </copyright>

namespace PrintCommon;

/// <summary>
/// A DTO for the upload/print information required by <see cref="Program.ExecuteAsync(ZplCommand, Connection)"/>.
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
/// Minimal interface for printing/uploading from a ZebraPrinter.
/// MUST BE .NET 9.0 (generic) compliant.
/// </summary>
public interface IPrintService
{
    /// <summary>
    /// Asynchronously execute the print/upload command specified in <paramref name="args"/>.
    /// </summary>
    /// <param name="args">The execution parameters (whether to upload/print, sample ID, upload/print path).</param>
    /// <returns>A Task representing that the upload/print is complete.</returns>
    public Task ExecuteAsync(ZplCommand args);
}
