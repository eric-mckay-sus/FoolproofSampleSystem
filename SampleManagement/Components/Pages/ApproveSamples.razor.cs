// <copyright file="ApproveSamples.razor.cs" company="Stanley Electric US Co. Inc.">
// Copyright (c) 2026 Stanley Electric US Co. Inc. Licensed under the MIT License.
// </copyright>

namespace SampleManagement.Components.Pages;

using BlazorBootstrap;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;
using SampleManagement.Components.Common;

/// <summary>
/// Code-behind for the sample approval page.
/// </summary>
public partial class ApproveSamples : TableManager<Sample>
{
    private Sample? pendingSample;
    private DateOnly? expiryDate = DateOnly.FromDateTime(DateTime.Today.AddYears(1));
    private bool isApproving;
    private string? approvalError;
    private int approverNum;

    /// <summary>
    /// Gets or sets the authentication state provider for accessing the current associate's number.
    /// </summary>
    [Inject]
    public AuthenticationStateProvider AuthStateProvider { get; set; } = default!;

    /// <summary>
    /// Gets the message to display when <see cref="TableManager{T}.DataView"/> is empty.
    /// </summary>
    public override string EmptyMessage => "No samples pending approval matching these filters";

    /// <summary>
    /// Gets or sets the dialog to show upon pressing the 'deny' button for a row.
    /// </summary>
    private protected DeleteDialog DeleteDialog { get; set; } = default!;

    /// <summary>
    /// When this page loads, set the sorting information, then let the parent set up.
    /// </summary>
    /// <returns>A Task representing that the page has loaded.</returns>
    protected override async Task OnInitializedAsync()
    {
        this.SortList.Add(new ("CreationDate", SortDir.Desc));
        this.SortList.Add(new ("SampleId", SortDir.Desc));

        // Resolve once — auth state is cached in the identity service, so we can assume it is stable for this session
        AuthenticationState authState = await this.AuthStateProvider.GetAuthenticationStateAsync();
        string? numClaim = authState.User.FindFirst("AssociateNum")?.Value;
        if (int.TryParse(numClaim, out int parsed))
        {
            this.approverNum = parsed;
        }

        await base.OnInitializedAsync();
    }

    /// <summary>
    /// Filters out already-approved samples (they are meaningless on the approval screen).
    /// Also applies the model/line filters, if applicable.
    /// </summary>
    /// <param name="query">The query to which filters should be applied.</param>
    /// <returns>A Task representing that <paramref name="query"/> is now filtered.</returns>
    protected override IQueryable<Sample> ApplyFilters(IQueryable<Sample> query)
    {
        query = query.Where(s => s.ApproverNum == null);

        if (this.ModelFilter.Value != null && this.ModelFilter.IsActive)
        {
            query = query.Where(x => x.Model.Contains(this.ModelFilter.Value));
        }

        if (this.LineFilter.Value != null && this.LineFilter.IsActive)
        {
            query = query.Where(x => x.Line.Contains(this.LineFilter.Value));
        }

        return query;
    }

    private void HandleApprove(Sample sample)
    {
        if (sample.Equals(this.pendingSample))
        {
            this.CancelApproval();
            return;
        }

        this.pendingSample = sample;
        this.expiryDate = DateOnly.FromDateTime(DateTime.Today.AddYears(1));
        this.approvalError = null;
    }

    private void CancelApproval()
    {
        this.pendingSample = null;
        this.approvalError = null;
    }

    private async Task ConfirmApproval()
    {
        if (this.pendingSample == null || this.expiryDate == default)
        {
            return;
        }

        this.isApproving = true;
        this.approvalError = null;

        try
        {
            using FPSampleDbContext context = await this.DbFactory.CreateDbContextAsync();
            await context.Database.ExecuteSqlInterpolatedAsync($@"
                EXEC [dbo].[ApproveSample]
                    @sampleID    = {this.pendingSample.SampleId},
                    @approverNum = {this.approverNum},
                    @expiryDate  = {this.expiryDate}");

            await this.RefreshData();
            this.ToastService.Notify(new (ToastType.Success, $"Sample #{this.pendingSample.SampleId} approved!"));
            this.pendingSample = null;
        }
        catch (Exception ex)
        {
            this.approvalError = $"Database error: {ex.Message}";
            this.ToastService.Notify(new (ToastType.Danger, "Approval failed."));
        }
        finally
        {
            this.isApproving = false;
        }
    }

    /// <summary>
    /// Shows the delete dialog, and if confirmed, remove from underlying table in the DB (then update view).
    /// </summary>
    /// <param name="sample">The sample to deny.</param>
    /// <returns>A Task representing that <paramref name="sample"/> has been removed and the view has been updated.</returns>
    private async Task HandleDeny(Sample sample)
    {
        if (await this.DeleteDialog.ConfirmAsync(sample))
        {
            // If the sample being denied was pending approval, close the approval window
            if (sample.Equals(this.pendingSample))
            {
                this.CancelApproval();
            }

            using FPSampleDbContext context = this.DbFactory.CreateDbContext();
            await context.Samples.Where(x => x.SampleId == sample.SampleId).ExecuteDeleteAsync();
            await this.RefreshData();
            this.ToastService.Notify(new (ToastType.Success, $"Successfully deleted sample #{sample.SampleId}"));
        }
    }
}
