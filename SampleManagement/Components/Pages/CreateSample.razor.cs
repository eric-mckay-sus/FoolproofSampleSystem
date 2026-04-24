// <copyright file="CreateSample.razor.cs" company="Stanley Electric US Co. Inc.">
// Copyright (c) 2026 Stanley Electric US Co. Inc. Licensed under the MIT License.
// </copyright>

namespace SampleManagement.Components.Pages;
using Microsoft.EntityFrameworkCore;
using ToastType = BlazorBootstrap.ToastType;

using PrintLabel;
using InterProcessIO;

/// <summary>
/// Code-behind for the CreateSample page.
/// </summary>
public partial class CreateSample : TableManager<Sample>
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
    /// The number of samples successfully printed in the current batch.
    /// </summary>
    private int printed = 0;

    /// <summary>
    /// The current batch size.
    /// </summary>
    private int totalFromQueue = 0;

    /// <summary>
    /// Flag to expand/collapse sample form.
    /// </summary>
    private bool isFormExpanded = false;

    /// <summary>
    /// Flag to switch between normal view and print select view.
    /// </summary>
    private bool printModeEngaged = false;

    /// <summary>
    /// Flag to prevent double-clicks while a print is processing.
    /// </summary>
    private bool isPrinting = false;

    /// <summary>
    /// The list of samples selected for printing.
    /// Could swap out List for HashSet, but the benefit here is that execution order matches selection order.
    /// </summary>
    private List<Sample> selectedForPrint = [];

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
        this.availableModels = this.allMappings.Select(m => m.ShortDescription).Distinct().OrderBy(x => x).ToList();
        this.availableLines = this.allMappings.Select(m => m.WorkCenterCode).Distinct().OrderBy(x => x).ToList();

        await base.RefreshData(keepPage);
    }

    /// <summary>
    /// When this page loads, set the sorting information, then let the parent set up.
    /// </summary>
    /// <returns>A Task representing that the page has loaded.</returns>
    protected override async Task OnInitializedAsync()
    {
        this.CurrentSortColumn = "CreationDate";
        this.SortDir = "descending";
        await base.OnInitializedAsync();
    }

    /// <summary>
    /// Filters the autofill lists based on what fields in the add form have values.
    /// </summary>
    /// <returns>A Task representing that filters have been refreshed.</returns>
    private async Task RefreshFilters()
    {
        using FPSampleDbContext context = this.DbFactory.CreateDbContext();

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
            this.availableModels = this.allMappings.Select(m => m.ShortDescription).Distinct().OrderBy(x => x).ToList();
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
            this.availableLines = this.allMappings.Select(m => m.WorkCenterCode).Distinct().OrderBy(x => x).ToList();
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

    private void TogglePrintMode()
    {
        this.printModeEngaged = !this.printModeEngaged;
        if (!this.printModeEngaged)
        {
            this.selectedForPrint.Clear(); // ensure selections do not persist between prints
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
        this.availableModels = this.allMappings.Select(m => m.ShortDescription).Distinct().OrderBy(x => x).ToList();
        this.availableLines = this.allMappings.Select(m => m.WorkCenterCode).Distinct().OrderBy(x => x).ToList();
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
            using FPSampleDbContext context = this.DbFactory.CreateDbContext();

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
            this.ToastService.Notify(new (ToastType.Success, "Sample created successfully!"));
        }
        catch (Exception ex)
        {
            this.errorMessage = $"Database Error: {ex.Message}";
            this.ToastService.Notify(new (ToastType.Danger, "Sample creation failed."));
        }
    }

    /// <summary>
    /// Prints one sample.
    /// </summary>
    /// <param name="sample">The <see cref="Sample"/> to print.</param>
    /// <returns>A Task representing that the print request has been issued (toast reports actual status).</returns>
    private async Task HandlePrint(Sample sample)
    {
        this.isPrinting = true;
        try
        {
            ZplCommand cmd = new () { IsPrint = true, SampleId = sample.SampleID };
            ZebraUploadPrint zupObject = new (this.InputProvider, this.Reporter);
            Report statusReport = await zupObject.ExecuteAsync(cmd);
            if (statusReport.level == ReportLevel.SUCCESS)
            {
                this.ToastService.Notify(new (ToastType.Success, $"Sample {sample.SampleID} sent to printer."));
            }
            else
            {
                this.ToastService.Notify(new (ToastType.Danger, statusReport.message));
            }
        }
        catch (Exception ex)
        {
            this.ToastService.Notify(new (ToastType.Danger, $"Print failed: {ex.Message}"));
        }
        finally
        {
            this.isPrinting = false;
        }
    }

    /// <summary>
    /// Prints all samples in <see cref="selectedForPrint"/>.
    /// </summary>
    /// <returns>A Task representing that all print requests have been issued.</returns>
    private async Task HandlePrint()
    {
        this.isPrinting = true;
        this.totalFromQueue = this.selectedForPrint.Count;
        HashSet<int> failedIds = [];
        foreach (Sample sample in this.selectedForPrint)
        {
            try
            {
                ZplCommand cmd = new () { IsPrint = true, SampleId = sample.SampleID };
                ZebraUploadPrint zupObject = new (this.InputProvider, this.Reporter);
                Report statusReport = await zupObject.ExecuteAsync(cmd);
                if (statusReport.level == ReportLevel.SUCCESS)
                {
                    this.ToastService.Notify(new (ToastType.Success, $"Sample #{sample.SampleID} sent to printer."));
                    this.printed++;
                }
                else
                {
                    this.ToastService.Notify(new (ToastType.Danger, $"Sample {sample.SampleID}: {statusReport.message}"));
                    failedIds.Add(sample.SampleID);
                }

                await Task.Delay(1000); // Wait a second between prints to ensure each toast is visible
            }
            catch (Exception ex)
            {
                this.ToastService.Notify(new (ToastType.Danger, $"Print failed for sample {sample.SampleID}: {ex.Message}"));
                failedIds.Add(sample.SampleID);
            }
        }

        // By setting selectedForPrint to only the failed IDs, the user can see easily which samples to investigate
        this.selectedForPrint = this.selectedForPrint.Where(x => failedIds.Contains(x.SampleID)).ToList();

        if (this.selectedForPrint.Count == 0)
        {
            this.ToastService.Notify(new (ToastType.Success, $"Successfully printed all {this.printed} samples!"));
            this.printModeEngaged = false;
        }
        else
        {
            this.ToastService.Notify(new (ToastType.Warning, $"Printed {this.printed} of {this.printed + failedIds.Count} samples (unsuccessful prints still selected)", $"Failed to print samples with IDs {string.Join(", ", failedIds)}"));
        }

        this.isPrinting = false;
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
