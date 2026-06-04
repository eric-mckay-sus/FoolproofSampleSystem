// <copyright file="SampleFormData.cs" company="Stanley Electric US Co. Inc.">
// Copyright (c) 2026 Stanley Electric US Co. Inc. Licensed under the MIT License.
// </copyright>

namespace SampleManagement;

using System.ComponentModel.DataAnnotations;

/// <summary>
/// Represents the data enclosed in the sample addition form
/// </summary>
[ValidateModelLineExists]
public record SampleFormData
{
    /// <summary>
    /// Gets or sets the new sample's model.
    /// </summary>
    [Required(ErrorMessage = "Model is required.")]
    [MaxLength(32, ErrorMessage = "Model must be 32 characters or fewer.")]
    [ValidateModelExists]
    public string Model { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the new sample's work center code (building and line name).
    /// </summary>
    [Required(ErrorMessage = "Line is required.")]
    [MaxLength(30, ErrorMessage = "Line must be 30 characters or fewer.")]
    [ValidateLineExists]
    public string Line { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the new sample's dummy sample number.
    /// Could validate that dummy sample number exists for model, but that's implicit in the provided choices, so it would never trigger.
    /// </summary>
    [Range(1, short.MaxValue, ErrorMessage = "Dummy sample number must be selected.")]
    public short DummySampleNum { get; set; } = 0;

    /// <summary>
    /// Gets or sets the new sample's creator name.
    /// </summary>
    [Required(ErrorMessage = "Associate number is required.")]
    [ValidateCreatorExists]
    public int? CreatorNum { get; set; }
}
