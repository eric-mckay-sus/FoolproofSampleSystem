// <copyright file="UniversalTable.razor.cs" company="Stanley Electric US Co. Inc.">
// Copyright (c) 2026 Stanley Electric US Co. Inc. Licensed under the MIT License.
// </copyright>

namespace SampleManagement.Components.Common;

using System.Reflection;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

/// <summary>
/// Defines the methods and state necessary to display the contents of <see cref="TableManager{T}"/>.
/// </summary>
/// <typeparam name="T">The class defining one record in the table.</typeparam>
public partial class UniversalTable<T>
    where T : class
{
    /// <summary>
    /// A cache of the properties of T so it's not invoked for each header, excluding those marked NotDisplayed.
    /// </summary>
    private readonly PropertyInfo[] cachedProps = typeof(T).GetProperties().Where(p => p.GetCustomAttribute<NotDisplayedAttribute>() == null).ToArray();

    /// <summary>
    /// Tracks whether the table is in expanded view.
    /// </summary>
    private bool isExpanded = false;

    /// <summary>
    /// Allows the user to highlight a row for focus (no information shown).
    /// </summary>
    private T? attentionItem;

    /// <summary>
    /// Binds to the text field "jump to".
    /// </summary>
    private string jumpPage = string.Empty;

    /// <summary>
    /// Debounce for the filters to avoid excessive DB hits.
    /// </summary>
    private CancellationTokenSource? filterDebounce;

    /// <summary>
    /// Gets or sets the JS handler to refocus the filter after debounce.
    /// </summary>
    [Inject]
    public IJSRuntime JS { get; set; } = default!;

    /// <summary>
    /// Gets or sets the data to display.
    /// </summary>
    [Parameter]
    public IEnumerable<T>? Items { get; set; }

    /// <summary>
    /// Gets or sets the message to display when <see cref="Items"/> is empty.
    /// </summary>
    [Parameter]
    public string EmptyMessage { get; set; } = "No data found. Please refresh.";

    /// <summary>
    /// Gets or sets a value indicating whether the query is loading.
    /// </summary>
    [Parameter]
    public bool IsLoading { get; set; } = false;

    /// <summary>
    /// Gets or sets the action to bind to the expand button being pressed.
    /// </summary>
    [Parameter]
    public EventCallback<T> OnExpand { get; set; }

    /// <summary>
    /// Gets or sets the row to highlight (because its information is shown).
    /// </summary>
    [Parameter]
    public T? Target { get; set; }

    /// <summary>
    /// Gets or sets the style to apply to <see cref="Target"/>.
    /// </summary>
    [Parameter]
    public string? TargetStyle { get; set; }

    // Filters

    /// <summary>
    /// Gets or sets the model filter.
    /// </summary>
    [Parameter]
    public Filter<string> ModelFilter { get; set; } = new Filter<string>("Model", string.Empty);

    /// <summary>
    /// Gets or sets the line filter.
    /// </summary>
    [Parameter]
    public Filter<string> LineFilter { get; set; } = new Filter<string>("Line", string.Empty);

    /// <summary>
    /// Gets or sets the action to perform when a filter changes.
    /// </summary>
    [Parameter]
    public EventCallback OnFilterChange { get; set; }

    /// <summary>
    /// Gets or sets the action to perform when the 'Clear Filters' button is pressed.
    /// </summary>
    [Parameter]
    public EventCallback ClearFilters { get; set; }

    // Sorting

    /// <summary>
    /// Gets or sets the action to perform when a sort column header is clicked.
    /// </summary>
    [Parameter]
    public EventCallback<string> OnSort { get; set; }

    /// <summary>
    /// Gets or sets the method used to fetch the sort icon for a target column.
    /// </summary>
    [Parameter]
    public Func<string, string> GetSortIcon { get; set; } = (col) => string.Empty;

    // Printing

    /// <summary>
    /// Gets or sets the action to bind to the print button being pressed.
    /// </summary>
    [Parameter]
    public EventCallback<T> OnPrint { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether print mode is engaged.
    /// </summary>
    [Parameter]
    public bool PrintModeEngaged { get; set; } = false;

    /// <summary>
    /// Gets or sets the list of <typeparamref name="T"/> objects which are selected to be printed.
    /// </summary>
    [Parameter]
    public List<T> SelectedForPrint { get; set; } = [];

    /// <summary>
    /// Gets or sets the action to call when <see cref="SelectedForPrint"/> changes.
    /// </summary>
    [Parameter]
    public EventCallback<List<T>> SelectedForPrintChanged { get; set; }

    // Approval

    /// <summary>
    /// Gets or sets the action to bind to approval.
    /// </summary>
    [Parameter]
    public EventCallback<T> OnApprove { get; set; }

    /// <summary>
    /// Gets or sets the action to bind to denial.
    /// </summary>
    [Parameter]
    public EventCallback<T> OnDeny { get; set; }

    // Remake request

    /// <summary>
    /// Gets or sets the action to bind to remake.
    /// </summary>
    [Parameter]
    public EventCallback<T> OnRemake { get; set; }

    // Pagination

    /// <summary>
    /// Gets or sets the current page number (must be between 1 and <see cref="TotalPages"/>, inclusive).
    /// </summary>
    [Parameter]
    public int CurrentPage { get; set; }

    /// <summary>
    /// Gets or sets the page count.
    /// </summary>
    [Parameter]
    public int TotalPages { get; set; }

    /// <summary>
    /// Gets or sets the total number of records retrieved.
    /// </summary>
    [Parameter]
    public int TotalCount { get; set; }

    /// <summary>
    /// Gets or sets the page size.
    /// </summary>
    [Parameter]
    public int PageSize { get; set; }

    /// <summary>
    /// Gets or sets the action to bind to page change.
    /// </summary>
    [Parameter]
    public EventCallback<int> OnPageChange { get; set; }

    /// <summary>
    /// Gets or sets the action to bind to page size change.
    /// </summary>
    [Parameter]
    public EventCallback<int> OnPageSizeChange { get; set; }

    // Denotes the range of records shown

    /// <summary>
    /// Gets the (ordinal) number of the first record currently in <see cref="Items"/>.
    /// </summary>
    private int StartRecord => ((this.CurrentPage - 1) * this.PageSize) + 1;

    /// <summary>
    /// Gets the (ordinal) number of the last record currently in <see cref="Items"/>.
    /// </summary>
    private int EndRecord => Math.Min(this.CurrentPage * this.PageSize, this.TotalCount);

    /// <summary>
    /// Gets a value indicating whether to show the actions column.
    /// </summary>
    private bool ShowActions => this.OnExpand.HasDelegate || this.OnPrint.HasDelegate || this.OnApprove.HasDelegate || this.OnRemake.HasDelegate;

    private async Task HandleExpand(T item)
    {
        this.isExpanded = !this.isExpanded;
        await this.OnExpand.InvokeAsync(item);
    }

    private void ToggleAttentionItem(T item)
    {
        // Turn off attention style if on
        if (this.attentionItem?.Equals(item) == true)
        {
            this.attentionItem = default;
        }

        // Turn on attention style if off (automatically revokes from current if one exists)
        else
        {
            this.attentionItem = item;
        }
    }

    private string GetRowClass(T item)
    {
        if (item.Equals(default))
        {
            return string.Empty;
        }

        // Priority 1: the row being targeted
        if (item.Equals(this.Target))
        {
            return this.TargetStyle ?? "table-primary";
        }

        // Priority 2: the row the user clicked to "watch"
        if (item.Equals(this.attentionItem))
        {
            return "table-active cursor-pointer";
        }

        return "cursor-pointer";
    }

    /// <summary>
    /// Gets the properties to display based on expansion state.
    /// Verbose properties are only shown when _isExpanded is true.
    /// If there's no OnExpand binding, Verbose columns remain permanently hidden.
    /// </summary>
    private PropertyInfo[] GetVisibleProperties()
    {
        // If expanded (and OnExpand is bound), show all columns; otherwise, hide Verbose columns
        if (this.OnExpand.HasDelegate && this.isExpanded)
        {
            return this.cachedProps;
        }

        return this.cachedProps.Where(p => p.GetCustomAttribute<VerboseAttribute>() == null).ToArray();
    }

    /// <summary>
    /// Gets the CSS class for the property.
    /// </summary>
    /// <param name="prop">The property to get the CSS class for.</param>
    /// <returns>The CSS class for the property.</returns>
    private string GetPropertyClass(PropertyInfo prop)
    {
        string className = $"col-{prop.Name}";

        // If this property is verbose, only show it in expanded mode
        if (prop.GetCustomAttribute<VerboseAttribute>() != null)
        {
            className += "transition-width";
            if (this.isExpanded)
            {
                className += "verbose-visible";
            }
            else
            {
                className += "verbose-hidden";
            }
        }

        return className;
    }

    /// <summary>
    /// Calls <see cref="GetSortIcon"/> to get the sort icon for <paramref name="col"/>.
    /// </summary>
    /// <param name="col">The column for which to get the sort icon.</param>
    /// <returns>The string to show denoting the sort order and priority.</returns>
    private string SortIcon(string col) => this.GetSortIcon?.Invoke(col) ?? string.Empty;

    /// <summary>
    /// To be called when a checkbox state changes.
    /// </summary>
    /// <param name="item">The item to be updated.</param>
    /// <param name="checkedValue">The new value for the checkbox.</param>
    /// <returns>A Task representing that <see cref="SelectedForPrint"/> has been synchronized.</returns>
    private async Task HandleCheckboxChange(T item, object? checkedValue)
    {
        bool isChecked = (bool)(checkedValue ?? false);

        List<T> updatedList = [.. this.SelectedForPrint ?? []]; // create a new object and manipulate that

        if (isChecked)
        {
            if (!updatedList.Contains(item))
            {
                updatedList.Add(item);
            }
        }
        else
        {
            updatedList.Remove(item);
        }

        this.SelectedForPrint = updatedList; // then assign it to the bound parameter to force Blazor to re-render

        // SelectedForPrint gets updated with updatedList as a parameter
        await this.SelectedForPrintChanged.InvokeAsync(updatedList);
    }

    /// <summary>
    /// For binding to the "jump to page" button.
    /// Converts <see cref="jumpPage"/> to an integer, checks boundaries, and calls <see cref="OnPageChange"/> to let the model handle the actual page change.
    /// </summary>
    /// <returns>A Task representing that the page has changed.</returns>
    private async Task HandleJumpPage()
    {
        if (int.TryParse(this.jumpPage, out int targetPage))
        {
            // Clamp the value between 1 and TotalPages
            targetPage = Math.Max(1, Math.Min(targetPage, this.TotalPages));
            this.jumpPage = targetPage.ToString(); // Update UI to show clamped value
            await this.OnPageChange.InvokeAsync(targetPage);
        }
    }

    /// <summary>
    /// Immediately updates the internal filter, then debounces the DB hit.
    /// </summary>
    /// <param name="filter">The filter being changed.</param>
    /// <param name="value">The new value for the filter.</param>
    /// <returns>A Task representing that the DB has been hit and the model is ready to re-render.</returns>
    private async Task HandleFilterInput(Filter<string> filter, string? value)
    {
        filter.Value = value; // Write immediately, no render yet

        if (this.filterDebounce != null)
        {
            await this.filterDebounce.CancelAsync();
            this.filterDebounce.Dispose();
        }

        this.filterDebounce = new CancellationTokenSource();
        CancellationToken token = this.filterDebounce.Token;

        try
        {
            await Task.Delay(300, token);
            await this.OnFilterChange.InvokeAsync(); // This triggers RefreshData in the parent, which calls StateHasChanged
            await this.JS.InvokeVoidAsync("focusElement", $"{filter.Key.ToLower()}-filter");
        }
        catch (OperationCanceledException)
        {
            // This means the debounce is debouncing
        }
    }
}
