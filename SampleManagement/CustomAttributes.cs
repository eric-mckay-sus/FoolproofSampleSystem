// <copyright file="CustomAttributes.cs" company="Stanley Electric US Co. Inc.">
// Copyright (c) 2026 Stanley Electric US Co. Inc. Licensed under the MIT License.
// </copyright>

namespace SampleManagement;

using System.ComponentModel.DataAnnotations;
using System.Reflection;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// Marks a property that should not be displayed in UniversalTable unless the table is expanded.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public class VerboseAttribute : Attribute
{
}

/// <summary>
/// Validates that a creator exists in the associate database.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public class ValidateCreatorExistsAttribute : ValidationAttribute
{
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
            return new ValidationResult($"Model {modelName} does not exist.", new[] { validationContext.MemberName ?? string.Empty });
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
            return new ValidationResult($"Line {lineName} does not exist.", new[] { validationContext.MemberName ?? string.Empty });
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
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        Console.WriteLine("Executing MTL check...");
        if (value == null)
        {
            Console.WriteLine("Returned for null value");
            return ValidationResult.Success;
        }

        // If one of model or line is empty, there can't be a mismatch
        PropertyInfo? modelProperty = validationContext.ObjectType.GetProperty("Model");
        PropertyInfo? lineProperty = validationContext.ObjectType.GetProperty("WorkCenterCode");
        if (modelProperty == null || lineProperty == null)
        {
            Console.WriteLine("Returned for null model or line");
            return ValidationResult.Success;
        }

        // Whitespace also can't violate the model-line match requirement
        string? model = modelProperty.GetValue(value) as string;
        string? line = lineProperty.GetValue(value) as string;
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
