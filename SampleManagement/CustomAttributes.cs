// <copyright file="CustomAttributes.cs" company="Stanley Electric US Co. Inc.">
// Copyright (c) 2026 Stanley Electric US Co. Inc. Licensed under the MIT License.
// </copyright>

namespace SampleManagement;

using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// Marks a property that should not be displayed in UniversalTable unless the table is expanded.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public class VerboseAttribute : Attribute
{
}

/// <summary>
/// Validates that a sample's creator exists.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public class ValidateCreatorExistsAttribute : ValidationAttribute
{
    /// <summary>
    /// Checks the new sample's creator signature against the database.
    /// </summary>
    /// <param name="value">The value to validate.</param>
    /// <param name="validationContext">The metadata for the validation.</param>
    /// <returns>ValidationResult.Success if the associate was found, otherwise a validation result reporting the failure.</returns>
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        if (value is not int creatorNum)
        {
            return new ValidationResult("Associate number is required.", new[] { validationContext.MemberName ?? string.Empty });
        }

        IDbContextFactory<FPSampleDbContext>? dbFactory = validationContext.GetService<IDbContextFactory<FPSampleDbContext>>();
        using FPSampleDbContext context = dbFactory!.CreateDbContext();

        if (!context.AssociateInfo.Any(a => a.AssociateNum == creatorNum))
        {
            return new ValidationResult($"Associate #{creatorNum} does not exist.", new[] { validationContext.MemberName ?? string.Empty });
        }

        return ValidationResult.Success;
    }
}

/// <summary>
/// Validates that a model exists in the foolproof data sheet table.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public class ValidateModelExistsAttribute : ValidationAttribute
{
    /// <summary>
    /// Checks the new sample's model against the database.
    /// </summary>
    /// <param name="value">The value to validate.</param>
    /// <param name="validationContext">The metadata for the validation.</param>
    /// <returns>ValidationResult.Success if the model was found, otherwise a validation result reporting the failure.</returns>
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        if (value is not string modelName)
        {
            return new ValidationResult("Model name is required.", new[] { validationContext.MemberName ?? string.Empty });
        }

        IDbContextFactory<FPSampleDbContext>? dbFactory = validationContext.GetService<IDbContextFactory<FPSampleDbContext>>();
        using FPSampleDbContext context = dbFactory!.CreateDbContext();

        if (!context.FoolproofInfo.Any(fp => fp.Model == modelName))
        {
            return new ValidationResult($"There are no foolproof data sheets uploaded for model {modelName}.", new[] { validationContext.MemberName ?? string.Empty });
        }

        return ValidationResult.Success;
    }
}

/// <summary>
/// Validates that a line exists in the model to line table.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public class ValidateLineExistsAttribute : ValidationAttribute
{
    /// <summary>
    /// Checks the new sample's line against the database.
    /// </summary>
    /// <param name="value">The value to validate.</param>
    /// <param name="validationContext">The metadata for the validation.</param>
    /// <returns>ValidationResult.Success if the line was found, otherwise a validation result reporting the failure.</returns>
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        if (value is not string lineName)
        {
            return new ValidationResult("Line name is required.", new[] { validationContext.MemberName ?? string.Empty });
        }

        IDbContextFactory<FPSampleDbContext>? dbFactory = validationContext.GetService<IDbContextFactory<FPSampleDbContext>>();
        using FPSampleDbContext context = dbFactory!.CreateDbContext();

        if (!context.ModelToLine.Any(mtl => mtl.Line == lineName))
        {
            return new ValidationResult($"There are no models uploaded that run on line {lineName}.", new[] { validationContext.MemberName ?? string.Empty });
        }

        return ValidationResult.Success;
    }
}

/// <summary>
/// Validates that the chosen model and line combination exists.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public class ValidateModelLineExistsAttribute : ValidationAttribute
{
    /// <summary>
    /// Cross-checks the new sample's model and line against the database.
    /// </summary>
    /// <param name="value">The value to validate.</param>
    /// <param name="validationContext">The metadata for the validation.</param>
    /// <returns>ValidationResult.Success if the model and line pair was found, otherwise a validation result reporting the failure.</returns>
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        Console.WriteLine("Executing MTL check...");

        // Pull the strongly-typed instance directly from the context safely
        if (validationContext.ObjectInstance is not SampleFormData formData)
        {
            Console.WriteLine("Returned because ObjectInstance is not SampleFormData");
            return ValidationResult.Success;
        }

        // Use the strongly-typed properties directly (No fragile reflection strings!)
        string? model = formData.Model;
        string? line = formData.Line;

        if (string.IsNullOrWhiteSpace(model) || string.IsNullOrWhiteSpace(line))
        {
            Console.WriteLine("Returned for empty model or line");
            return ValidationResult.Success;
        }

        IDbContextFactory<FPSampleDbContext>? dbFactory = validationContext.GetService<IDbContextFactory<FPSampleDbContext>>();
        using FPSampleDbContext context = dbFactory!.CreateDbContext();

        // If there's no record with this model AND line, the validation fails
        if (!context.ModelToLine.Any(x => x.Model == model && x.Line == line))
        {
            Console.WriteLine("MTL check failed");
            return new ValidationResult($"Model '{model}' is not valid for line '{line}'. Please adjust them to match.");
        }

        Console.WriteLine("Match found in DB");
        return ValidationResult.Success;
    }
}

/// <summary>
/// Marks a property that should not be displayed in UniversalTable.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public class NotDisplayedAttribute : Attribute
{
}
