// <copyright file="CreateSample.razor.cs" company="Stanley Electric US Co. Inc.">
// Copyright (c) 2026 Stanley Electric US Co. Inc. Licensed under the MIT License.
// </copyright>

namespace SampleManagement.Components.Pages;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// Code-behind for the CreateSample page.
/// </summary>
public partial class CreateSample
{
    /// <summary>
    /// List of all entries in the MTL table.
    /// </summary>
    private IList<ModelLine> allMappings = [];

    /// <summary>
    /// The pending sample to be added upon validation.
    /// </summary>
    private SampleFormData formData = new ();

    // Filtered lists to use for autofill

    /// <summary>
    /// The list of all models available for the current line.
    /// </summary>
    private IList<string> availableModels = [];

    /// <summary>
    /// The list of all lines available for the current model.
    /// </summary>
    private IList<string> availableLines = [];

    /// <summary>
    /// The list of all dummy sample numbers avaiable for the current model.
    /// </summary>
    private List<short> availableSampleNums = [];

    // UI properties

    /// <summary>
    /// Flag to expand/collapse sample form.
    /// </summary>
    private bool isFormExpanded = false;

    /// <summary>
    /// Error message about pending sample, if applicable.
    /// </summary>
    private string? errorMessage;

    /// <summary>
    /// Gets a value indicating whether the sample form is ready for a dummy sample number.
    /// </summary>
    private bool NotReadyForSampleNum =>
        string.IsNullOrWhiteSpace(this.formData.Model) ||
        string.IsNullOrWhiteSpace(this.formData.WorkCenterCode);

    /// <summary>
    /// Gets a value indicating whether the sample form is ready for associate signature.
    /// </summary>
    private bool NotReadyForSignature =>
        this.NotReadyForSampleNum ||
        this.formData.DummySampleNum < 1;

    /// <summary>
    /// Resets the filters and fetches the sample table.
    /// </summary>
    /// <param name="keepPage"><inheritdoc/></param>
    /// <returns>A Task representing that data has been successfully refreshed.</returns>
    public override async Task RefreshData(bool keepPage = false)
    {
        using FPSampleDbContext context = this.DbFactory.CreateDbContext();

        // Fetch the mapping table once to handle bidirectional filtering in memory
        this.allMappings = await context.ModelToLine.ToListAsync();

        // Initialize the UI lists with everything
        this.availableModels = this.allMappings.Select(m => m.ShortDescription).Distinct().ToList();
        this.availableLines = this.allMappings.Select(m => m.WorkCenterCode).Distinct().ToList();

        await base.RefreshData(keepPage);
    }

    /// <summary>
    /// Filters the autofill lists based on what fields in the add form have values.
    /// </summary>
    /// <returns>A Task representing that filters have been refreshed.</returns>
    private async Task RefreshFilters()
    {
        using var context = this.DbFactory.CreateDbContext();

        // Normalize inputs to handle casing and extra whitespace
        string searchModel = this.formData.Model.Trim();
        string searchLine = this.formData.WorkCenterCode.Trim();

        bool hasModel = !string.IsNullOrEmpty(searchModel);
        bool hasLine = !string.IsNullOrEmpty(searchLine);

        // If line is selected, use it for filtering
        if (hasLine && !hasModel)
        {
            this.availableModels = this.allMappings
                .Where(x => x.WorkCenterCode == this.formData.WorkCenterCode)
                .Select(x => x.ShortDescription)
                .OrderBy(x => x)
                .Distinct().ToList();
        }

        // Otherwise, clear the line filter
        else if (!hasLine && !hasModel)
        {
            this.availableModels = this.allMappings.Select(m => m.ShortDescription).Distinct().ToList();
        }

        // If model is selected, use it for filtering
        if (hasModel && !hasLine)
        {
            this.availableLines = this.allMappings
                .Where(x => x.ShortDescription == this.formData.Model)
                .Select(x => x.WorkCenterCode)
                .OrderBy(x => x)
                .Distinct().ToList();
        }

        // Otherwise, clear the model filter
        else if (!hasLine && !hasModel)
        {
            this.availableLines = this.allMappings.Select(m => m.WorkCenterCode).Distinct().ToList();
        }

        // Update sample numbers when model is selected
        if (hasModel)
        {
            this.availableSampleNums = await context.FoolproofInfo
                .Where(f => f.Model == this.formData.Model)
                .Select(f => f.DummySampleNum)
                .OrderBy(x => x)
                .Distinct().ToListAsync();
        }
        else
        {
            this.availableSampleNums.Clear();
            this.formData.DummySampleNum = 0;
        }
    }

    /// <summary>
    /// Remove add form flag, clear input, error message and autofill list filters.
    /// </summary>
    private void CloseForm()
    {
        this.isFormExpanded = false;
        this.formData = new ();
        this.errorMessage = null;

        // Reload available lists
        this.availableModels = this.allMappings.Select(m => m.ShortDescription).Distinct().ToList();
        this.availableLines = this.allMappings.Select(m => m.WorkCenterCode).Distinct().ToList();
    }

    /// <summary>
    /// Attempts to run the stored procedure with the current form input, populating error message as necessary.
    /// </summary>
    /// <returns>A Task representing successful submission.</returns>
    private async Task HandleSubmit()
    {
        this.errorMessage = null; // Ensure any error messages are for this submission

        try
        {
            using var context = this.DbFactory.CreateDbContext();

            // ExecuteSqlInterpolatedAsync internally wraps each parameter in an injection-safe DbParameter
            await context.Database.ExecuteSqlInterpolatedAsync($@"
            EXEC [dbo].[CreateSample]
                @model = {this.formData.Model},
                @workCenterCode = {this.formData.WorkCenterCode},
                @dummySampleNum = {this.formData.DummySampleNum},
                @creatorName = {this.formData.CreatorName}");

            this.formData = new (); // Reset form
            await this.RefreshData();
            this.isFormExpanded = false; // Auto-collapse on success to show the table
        }
        catch (Exception ex)
        {
            this.errorMessage = $"Database Error: {ex.Message}";
        }
    }

    /// <summary>
    /// Represents the data enclosed in the sample addition form
    /// </summary>
    public record SampleFormData
    {
        /// <summary>
        /// Gets or sets the new sample's model.
        /// </summary>
        public string Model { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the new sample's work center code (building and line name).
        /// </summary>
        public string WorkCenterCode { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the new sample's dummy sample number.
        /// </summary>
        public short DummySampleNum { get; set; } = 0;

        /// <summary>
        /// Gets or sets the new sample's creator name.
        /// </summary>
        public string CreatorName { get; set; } = string.Empty;
    }
}
