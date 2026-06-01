// <copyright file="InterProcessIoTests.cs" company="Stanley Electric US Co. Inc.">
// Copyright (c) 2026 Stanley Electric US Co. Inc. Licensed under the MIT License.
// </copyright>

namespace FoolproofSampleSystem.Tests;

using InterProcessIO;

public sealed class InterProcessIoTests
{
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
