// <copyright file="InterProcessIoTests.cs" company="Stanley Electric US Co. Inc.">
// Copyright (c) 2026 Stanley Electric US Co. Inc. Licensed under the MIT License.
// </copyright>

namespace FoolproofSampleSystem.Tests;

using InterProcessIO;

/// <summary>
/// Tests for the inter-process I/O providers.
/// </summary>
public sealed class InterProcessIoTests
{
    /// <summary>
    /// Verifies that the overridden OR operator takes the union of each flag.
    /// </summary>
    [Fact]
    public void ParseResult_OrOperatorCombinesFlags()
    {
        ParseResult left = new (hasDuplicate: true);
        ParseResult right = new (hasFormatError: true, hasMiscError: true, alreadyUploaded: true);

        ParseResult combined = left | right;

        Assert.True(combined.HasDuplicate);
        Assert.True(combined.HasFormatError);
        Assert.True(combined.HasMiscError);
        Assert.True(combined.AlreadyUploaded);
    }
}
