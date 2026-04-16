// <copyright file="TableManager.cs" company="Stanley Electric US Co. Inc.">
// Copyright (c) 2026 Stanley Electric US Co. Inc. Licensed under the MIT License.
// </copyright>

namespace SampleManagement;

using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using ToastService = BlazorBootstrap.ToastService;
using System.Linq.Dynamic.Core;

/// <summary>
/// Minimal table logic for loading and paging data from <see cref="FPSampleDbContext"/>.
/// Designed to provide the data needed by <c>UniversalTable</c> without filters or UI-only services.
/// </summary>
/// <typeparam name="T">The EF entity type to load.</typeparam>
public class TableManager<T> : ComponentBase
    where T : class
{
    /// <summary>
    /// Gets the data from the DB table with rows of type <typeparamref name="T" /> (there should only be one).
    /// </summary>
    public List<T> DataView { get; private set; } = [];

    /// <summary>
    /// Gets a value indicating whether the table is loading.
    /// </summary>
    public bool IsLoading { get; private set; }

    /// <summary>
    /// Gets the current page number (always clamped between 0 and <see cref="TotalPages"/>, inclusive).
    /// </summary>
    public int CurrentPage { get; private set; } = 1;

    /// <summary>
    /// Gets or sets the number of rows on one page.
    /// </summary>
    public int PageSize { get; set; } = 50;

    /// <summary>
    /// Gets the total number of rows retrieved by the current query.
    /// </summary>
    public int TotalCount { get; private set; }

    /// <summary>
    /// Gets the number of pages retrieved by the current query (total rows divided by page size).
    /// </summary>
    public int TotalPages => this.PageSize > 0 ? (int)Math.Ceiling((double)this.TotalCount / this.PageSize) : 1;

    /// <summary>
    /// Gets or sets the name of the column that results are currently being sorted by.
    /// </summary>
    public string CurrentSortColumn { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the sort direction of the currently sorted column.
    /// </summary>
    public string SortDir { get; set; } = "none";

    /// <summary>
    /// Gets or sets the thread-safe DB context generator.
    /// </summary>
    [Inject]
    private protected IDbContextFactory<FPSampleDbContext> DbFactory { get; set; } = default!;

    /// <summary>
    /// Gets or sets the toast service for displaying success/failure messages.
    /// </summary>
    [Inject]
    private protected ToastService ToastService { get; set; } = default!;

    /// <summary>
    /// Loads the current page of data from the database.
    /// </summary>
    /// <param name="keepPage">Whether to keep the current page number.</param>
    /// <returns>The task for the load operation.</returns>
    public virtual async Task RefreshData(bool keepPage = false)
    {
        if (!keepPage)
        {
            this.CurrentPage = 1;
        }

        this.IsLoading = true;

        try
        {
            using FPSampleDbContext context = await this.DbFactory.CreateDbContextAsync();
            IQueryable<T> query = context.Set<T>().AsNoTracking();

            query = this.ApplySorting(query);
            this.TotalCount = await query.CountAsync();
            this.DataView = await query
                .Skip((this.CurrentPage - 1) * this.PageSize)
                .Take(this.PageSize)
                .ToDynamicListAsync<T>();
        }
        finally
        {
            this.IsLoading = false;
        }
    }

    /// <summary>
    /// Cycles through sort directions when column is toggled
    /// Cycle order: None -> Asc -> Desc.
    /// </summary>
    /// <param name="columnName">The column to be toggled.</param>
    /// <returns>A Task representing that the sort has been applied.</returns>
    public async Task ToggleSort(string columnName)
    {
        if (this.CurrentSortColumn != columnName)
        { // If coming from none, save the column name (it's changed) and switch to asc
            this.CurrentSortColumn = columnName;
            this.SortDir = "ascending";
        }
        else if (this.SortDir == "ascending")
        { // If coming from asc, only need to switch to desc
            this.SortDir = "descending";
        }
        else
        { // If coming from desc, switch to none and inform model no column is specified to sort
            this.SortDir = "none";
            this.CurrentSortColumn = string.Empty;
        }

        await this.RefreshData(); // because the sort parameters change we want a guaranteed refresh
    }

    /// <summary>
    /// Helper to render the arrow.
    /// </summary>
    /// <param name="columnName">The column for which to update the sort icon.</param>
    /// <returns>The Unicode arrow representing the sort direction.</returns>
    public string GetSortIcon(string columnName)
    {
        if (this.CurrentSortColumn != columnName || this.SortDir == "none")
        {
            return "↕";
        }

        return this.SortDir == "ascending" ? "▲" : "▼";
    }

    /// <summary>
    /// Switches to a specific page and reloads data.
    /// </summary>
    /// <param name="newPage">The page to change to.</param>
    /// <returns>A Task representing that the page has been changed.</returns>
    public async Task ChangePage(int newPage)
    {
        if (newPage < 1)
        {
            newPage = 1;
        }

        if (newPage == this.CurrentPage)
        {
            return;
        }

        this.CurrentPage = newPage;
        await this.RefreshData(keepPage: true);
    }

    /// <summary>
    /// Changes the page size and reloads from the first page.
    /// </summary>
    /// <param name="newSize">The page size to change to.</param>
    /// <returns>A Task representing that the page size has been changed.</returns>
    public async Task AlterPageSize(int newSize)
    {
        if (newSize <= 0 || newSize == this.PageSize)
        {
            return;
        }

        this.PageSize = newSize;
        this.CurrentPage = 1;
        await this.RefreshData(keepPage: true);
    }

    /// <summary>
    /// Clears the in-memory table state.
    /// </summary>
    public void ClearData()
    {
        this.DataView.Clear();
        this.TotalCount = 0;
        this.CurrentPage = 1;
    }

    /// <summary>
    /// When the page loads, perform an initial load for the corresponding table.
    /// </summary>
    /// <returns>A Task representing that the page has loaded.</returns>
    protected override async Task OnInitializedAsync() => await this.RefreshData();

    /// <summary>
    /// Uses dynamic LINQ to draft a SQL ORDER BY based on the current sort.
    /// </summary>
    /// <param name="query">The query to which the sorts should be appended.</param>
    /// <returns>An IQueryable object with sorts applied.</returns>
    private IQueryable<T> ApplySorting(IQueryable<T> query)
    {
        if (this.SortDir == "none" || string.IsNullOrWhiteSpace(this.CurrentSortColumn))
        {
            return query;
        }

        // Null is the smallest value for any column, so it clutters ascending sorts
        return query.Where($"{this.CurrentSortColumn} != null").OrderBy($"{this.CurrentSortColumn} {this.SortDir}");
    }
}
