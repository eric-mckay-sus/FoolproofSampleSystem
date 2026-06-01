// <copyright file="ModelValidator.cs" company="Stanley Electric US Co. Inc.">
// Copyright (c) 2026 Stanley Electric US Co. Inc. Licensed under the MIT License.
// </copyright>

namespace UploadFpInfo;

/// <summary>
/// Validates that a model name exists in the model-to-line database.
/// </summary>
public interface IModelValidator
{
    /// <summary>
    /// Verifies that a particular model exists in the model to line (MTL) database.
    /// </summary>
    /// <param name="modelName">The model name to validate.</param>
    /// <returns>The canonical model name from the database, or null when it is missing.</returns>
    Task<string?> ValidateAsync(string? modelName);
}

/// <summary>
/// SQL Server implementation of <see cref="IModelValidator"/>.
/// </summary>
public sealed class SqlModelValidator : IModelValidator
{
    /// <inheritdoc/>
    public Task<string?> ValidateAsync(string? modelName) => DbUploadUtilities.ValidateModel(modelName);
}
