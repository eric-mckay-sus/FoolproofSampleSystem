// <copyright file="FpPreviewRow.cs" company="Stanley Electric US Co. Inc.">
// Copyright (c) 2026 Stanley Electric US Co. Inc. Licensed under the MIT License.
// </copyright>

namespace UploadFpInfo;
using System.ComponentModel.DataAnnotations.Schema;

/// <summary>
/// A DTO that represents a single row of just-uploaded FP data for display in UniversalTable.
/// </summary>
public class FoolproofPreviewRow
{
    /// <summary>
    /// Gets or sets the FP entry's model name.
    /// </summary>
    [Column("Model")]
    public string? Model { get; set; }

    /// <summary>
    /// Gets or sets the FP entry's process failure mode description.
    /// </summary>
    [Column("Failure Mode")]
    public string? FailureMode { get; set; }

    /// <summary>
    /// Gets or sets the FP entry's machine type within the line.
    /// </summary>
    [Column("Location")]
    public string? Location { get; set; }

    /// <summary>
    /// Gets or sets the FP entry's dummy sample number.
    /// </summary>
    [Column("Dummy #")]
    public string? DummySampleNum { get; set; }
}
