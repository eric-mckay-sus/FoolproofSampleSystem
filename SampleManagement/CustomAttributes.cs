// <copyright file="CustomAttributes.cs" company="Stanley Electric US Co. Inc.">
// Copyright (c) 2026 Stanley Electric US Co. Inc. Licensed under the MIT License.
// </copyright>

namespace SampleManagement;

/// <summary>
/// Marks a property that should not be displayed in UniversalTable unless the table is expanded.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public class VerboseAttribute : Attribute
{
}

/// <summary>
/// Marks a property that should not be displayed in UniversalTable.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public class NotDisplayedAttribute : Attribute
{
}
