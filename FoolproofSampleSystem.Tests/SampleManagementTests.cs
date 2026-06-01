// <copyright file="SampleManagementTests.cs" company="Stanley Electric US Co. Inc.">
// Copyright (c) 2026 Stanley Electric US Co. Inc. Licensed under the MIT License.
// </copyright>

namespace FoolproofSampleSystem.Tests;

using Microsoft.EntityFrameworkCore;
using SampleManagement;
using SampleManagement.Components.Pages;

public sealed class SampleManagementTests
{
    [Fact]
    public void Sort_ToggleCyclesThroughDirections()
    {
        Sort sort = new ("Model", SortDir.Asc);

        Assert.True(sort.Toggle());
        Assert.Equal(SortDir.Desc, sort.Direction);

        Assert.False(sort.Toggle());
        Assert.Equal(SortDir.None, sort.Direction);
    }

    [Fact]
    public void Filter_TracksActivityAndCanReset()
    {
        Filter<string> filter = new ("Model", null);

        Assert.False(filter.IsActive);

        filter.Value = "   ";
        Assert.False(filter.IsActive);

        filter.Value = "ALPHA";
        Assert.True(filter.IsActive);
        Assert.Equal("Model: ALPHA (active)", filter.ToString());

        filter.Reset();
        Assert.False(filter.IsActive);
        Assert.Null(filter.Value);
    }

    [Fact]
    public async Task ToggleSort_CyclesIconsWithoutRefreshingLocalhostOrDatabase()
    {
        TestableTableManager table = new ();

        await table.ToggleSort("Model");
        Assert.Equal("▲", table.GetSortIcon("Model"));
        Assert.Equal(1, table.RefreshCount);

        await table.ToggleSort("Line");
        Assert.Equal("▲₂", table.GetSortIcon("Line"));

        await table.ToggleSort("Model");
        Assert.Equal("▼", table.GetSortIcon("Model"));

        await table.ToggleSort("Model");
        Assert.Equal("↕", table.GetSortIcon("Model"));
    }

    [Fact]
    public void ApproveSamplesApplyFilters_ReturnsOnlyUnapprovedRowsMatchingModelAndLine()
    {
        TestableApproveSamples page = new ()
        {
            ModelFilter = new ("Model", "ALPHA"),
            LineFilter = new ("Line", "L1"),
        };
        IQueryable<Sample> samples = new[]
        {
            MakeSample(1, "ALPHA", "L1", approverNum: null),
            MakeSample(2, "ALPHA", "L2", approverNum: null),
            MakeSample(3, "BETA", "L1", approverNum: null),
            MakeSample(4, "ALPHA", "L1", approverNum: 1001),
        }.AsQueryable();

        List<int> visibleIds = page.ApplyFiltersForTest(samples).Select(sample => sample.SampleId).ToList();

        Assert.Equal([1], visibleIds);
    }

    [Fact]
    public void CreateSampleApplyFilters_ReturnsOnlyUnapprovedRowsMatchingModelAndLine()
    {
        TestableCreateSample page = new ()
        {
            ModelFilter = new ("Model", "ALPHA"),
            LineFilter = new ("Line", "L1"),
        };
        IQueryable<Sample> samples = new[]
        {
            MakeSample(1, "ALPHA", "L1", approverNum: null),
            MakeSample(2, "ALPHA", "L2", approverNum: null),
            MakeSample(3, "BETA", "L1", approverNum: null),
            MakeSample(4, "ALPHA", "L1", approverNum: 1001),
        }.AsQueryable();

        List<int> visibleIds = page.ApplyFiltersForTest(samples).Select(sample => sample.SampleId).ToList();

        Assert.Equal([1], visibleIds);
    }

    [Fact]
    public async Task DbContext_CanUseInMemoryDatabaseForSampleQueries()
    {
        DbContextOptions<FPSampleDbContext> options = new DbContextOptionsBuilder<FPSampleDbContext>()
            .UseInMemoryDatabase($"fpsamples-{Guid.NewGuid():N}")
            .Options;

        await using (FPSampleDbContext seedContext = new (options))
        {
            seedContext.Samples.AddRange(
                MakeSample(1, "ALPHA", "L1", approverNum: null),
                MakeSample(2, "BETA", "L2", approverNum: 2002));
            await seedContext.SaveChangesAsync();
        }

        await using FPSampleDbContext queryContext = new (options);
        List<Sample> pending = await queryContext.Samples.Where(sample => sample.ApproverNum == null).ToListAsync();

        Sample sample = Assert.Single(pending);
        Assert.Equal("ALPHA", sample.Model);
    }

    private static Sample MakeSample(int id, string model, string line, int? approverNum) => new ()
    {
        SampleId = id,
        DummySampleNum = (short)(id + 10),
        Model = model,
        Rank = 'A',
        Line = line,
        Iteration = 1,
        CreationDate = new DateOnly(2026, 1, id),
        FailureMode = "Failure",
        Location = "Location",
        CreatorNum = 1000 + id,
        ApproverNum = approverNum,
        IsActive = true,
    };

    private sealed class TestableTableManager : TableManager<Sample>
    {
        public int RefreshCount { get; private set; }

        public override Task RefreshData(bool keepPage = false)
        {
            this.RefreshCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class TestableApproveSamples : ApproveSamples
    {
        public IQueryable<Sample> ApplyFiltersForTest(IQueryable<Sample> query) => this.ApplyFilters(query);
    }

    private sealed class TestableCreateSample : CreateSample
    {
        public IQueryable<Sample> ApplyFiltersForTest(IQueryable<Sample> query) => this.ApplyFilters(query);
    }
}
