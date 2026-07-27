// <copyright file="CreateSample.razor.cs" company="Stanley Electric US Co. Inc.">
// Copyright (c) 2026 Stanley Electric US Co. Inc. Licensed under the MIT License.
// </copyright>

namespace SampleManagement.Components.Pages;
using System.ComponentModel.DataAnnotations;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using ToastType = BlazorBootstrap.ToastType;

using PrintLabel;
using InterProcessIO;
using System.Net.Sockets;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components;

/// <summary>
/// Code-behind for the CreateSample page.
/// </summary>
public partial class CreateSample : TableManager<Sample>
{
    /// <summary>
    /// The pending sample to be added upon validation.
    /// </summary>
    private readonly SampleFormData formData = new ();

    /// <summary>
    /// Materialized list of all distinct model-line pairs in the MTL table for simple filtering without DB interaction.
    /// </summary>
    private IList<(string Model, string Line)> allMappings = [];

    private EditContext? editContext;

    private ValidationMessageStore? messageStore;

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

    /// <summary>
    /// The list of dummy sample numbers selected for batch creation.
    /// </summary>
    private List<short> selectedDummySampleNums = [];

    /// <summary>
    /// The last model name that was entered in the sample creation card.
    /// </summary>
    private string lastModel = string.Empty;

    // For printing

    /// <summary>
    /// The DPI to with which to print samples.
    /// </summary>
    private int printDpi = Config.PrinterDpi;

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
    /// Tracks whether the user is viewing the 'print new sample?' prompt.
    /// </summary>
    private bool isAwaitingPrint = false;

    /// <summary>
    /// Stores the <see cref="Sample"/> object that was most recently created in this session, if applicable.
    /// </summary>
    private Sample? justCreatedSample;

    // UI properties

    /// <summary>
    /// Error message about pending sample, if applicable.
    /// </summary>
    private string? errorMessage;

    /// <summary>
    /// Allows for cancelling mid-print.
    /// </summary>
    private CancellationTokenSource? printCts;

    /// <summary>
    /// Gets the message to display when <see cref="TableManager{T}.DataView"/> is empty.
    /// </summary>
    public override string EmptyMessage => "No samples matching these filters.";

    [Inject]
    private IServiceProvider ServiceProvider { get; set; } = default!;

    /// <summary>
    /// Gets a value indicating whether the sample form is ready for a dummy sample number.
    /// </summary>
    private bool NotReadyForSampleNum =>
        string.IsNullOrWhiteSpace(this.formData.Model) ||
        string.IsNullOrWhiteSpace(this.formData.Line) ||
        this.availableSampleNums.Count == 0;

    /// <summary>
    /// Gets a value indicating whether the sample form is ready for associate signature.
    /// </summary>
    private bool NotReadyForSignature =>
        this.NotReadyForSampleNum ||
        this.selectedDummySampleNums.Count == 0;

    /// <summary>
    /// When this page loads, set the sorting information, then let the parent set up.
    /// </summary>
    /// <remarks>Because this.allMappings is only populated on page load, recent data can only be retrieved with a refresh.</remarks>
    /// <returns>A Task representing that the page has loaded.</returns>
    protected override async Task OnInitializedAsync()
    {
        this.editContext = new (this.formData);
        this.messageStore = new (this.editContext);

        // Get all the model-line pairs that have a model in the foolproof sheet database (thus have a dummy sample number)
        using (FPSampleDbContext context = await this.DbFactory.CreateDbContextAsync())
        {
            this.allMappings = await context.ModelToLine
                                    .AsNoTracking()
                                    .Join(
                                        context.FoolproofInfo,
                                        mtl => mtl.Model,
                                        fp => fp.Model,
                                        (mtl, fp) => new { mtl.Model, mtl.Line })
                                    .Select(x => ValueTuple.Create(x.Model, x.Line))
                                    .Distinct()
                                    .ToListAsync();
        }

        // Initialize the model/line lists with everything from allMappings
        this.availableModels = this.allMappings
                                    .Select(m => m.Model)
                                    .Distinct()
                                    .OrderBy(x => x)
                                    .ToList();

        this.availableLines = this.allMappings
                                    .Select(m => m.Line)
                                    .Distinct()
                                    .OrderBy(x => x)
                                    .ToList();

        this.SortList.Add(new ("CreationDate", SortDir.Desc));
        this.SortList.Add(new ("SampleId", SortDir.Desc));
        await base.OnInitializedAsync();
    }

    /// <summary>
    /// Filters out samples samples that are approved, so they cannot be reprinted.
    /// Also applies model/line filters if applicable.
    /// </summary>
    /// <param name="query"><inheritdoc/></param>
    /// <returns>The <paramref name="query"/> where remake date is null.</returns>
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

    /// <summary>
    /// Fires a SqlCommand to execute the sample creation stored procedure.
    /// </summary>
    /// <param name="context">The DB context where the SP lives.</param>
    /// <param name="data">The data used in the sample creation SP.</param>
    /// <returns>A Task-wrapped integer representing the ID of the new sample.</returns>
    private static async Task<int> CreateSampleAndFetchId(FPSampleDbContext context, SampleFormData data)
    {
        await context.Database.OpenConnectionAsync();
        using SqlCommand cmd = new (
            "EXEC [dbo].[CreateSample] @model, @workCenterCode, @dummySampleNum, @creatorNum",
            (SqlConnection)context.Database.GetDbConnection());

        cmd.Parameters.AddWithValue("@model", data.Model);
        cmd.Parameters.AddWithValue("@workCenterCode", data.Line);
        cmd.Parameters.AddWithValue("@dummySampleNum", data.DummySampleNum);
        cmd.Parameters.AddWithValue("@creatorNum", data.CreatorNum);

        object? result = await cmd.ExecuteScalarAsync();
        return Convert.ToInt32(result); // Must be an integer, as the SP selects SCOPE_IDENTITY()
    }

    /// <summary>
    /// Navigates to the remake page with the desired sample.
    /// </summary>
    /// <param name="sample">The sample for which to request a remake.</param>
    private void HandleNavigateToRemake(Sample sample) => this.Navigation.NavigateTo($"/request-remake?sampleId={sample.SampleId}");

    /// <summary>
    /// Filters the autofill lists based on what fields in the add form have values.
    /// </summary>
    /// <returns>A Task representing that filters have been refreshed.</returns>
    private async Task RefreshFilters()
    {
        using FPSampleDbContext context = await this.DbFactory.CreateDbContextAsync();

        // Normalize inputs to handle casing and extra whitespace
        string searchModel = this.formData.Model.Trim();
        string searchLine = this.formData.Line.Trim();

        bool hasModel = !string.IsNullOrEmpty(searchModel);
        bool hasLine = !string.IsNullOrEmpty(searchLine);

        switch (hasModel, hasLine)
        {
            // If there's no model or line, clear any existing filters
            case (false, false):
                this.availableLines = this.allMappings.Select(m => m.Line).Distinct().OrderBy(x => x).ToList();
                this.availableModels = this.allMappings.Select(m => m.Model).Distinct().OrderBy(x => x).ToList();
                break;

            // If line is selected, use it for filtering the models
            case (false, true):
                this.availableModels = this.allMappings
                    .Where(x => x.Line == searchLine)
                    .Select(x => x.Model)
                    .Distinct()
                    .OrderBy(x => x)
                    .ToList();
                break;

            // If model is selected, use it for filtering the lines
            case (true, false):
                this.availableLines = this.allMappings
                    .Where(x => x.Model == searchModel)
                    .Select(x => x.Line)
                    .Distinct()
                    .OrderBy(x => x)
                    .ToList();
                break;
        }

        // Update sample numbers when model is selected
        bool modelChanged = hasModel && searchModel != this.lastModel;
        if (hasModel)
        {
            // Only hit DB when it's a new model
            if (modelChanged)
            {
                this.availableSampleNums = await context.FoolproofInfo
                    .Where(f => f.Model == searchModel)
                    .Select(f => f.DummySampleNum)
                    .Distinct()
                    .OrderBy(x => x)
                    .ToListAsync();
                this.lastModel = searchModel;
            }
        }
        else
        {
            this.availableSampleNums.Clear();
            this.formData.DummySampleNum = 0;
            this.selectedDummySampleNums.Clear();
        }

        if (this.NotReadyForSampleNum)
        {
            this.formData.DummySampleNum = 0;
            this.selectedDummySampleNums.Clear();

            if (this.editContext != null)
            {
                var fieldIdentifier = FieldIdentifier.Create(() => this.formData.DummySampleNum);

                this.editContext.MarkAsUnmodified(fieldIdentifier);
            }
        }
        else if (modelChanged)
        {
            this.selectedDummySampleNums.Clear();
        }
        else
        {
            this.selectedDummySampleNums = this.selectedDummySampleNums
                .Where(num => this.availableSampleNums.Contains(num))
                .Distinct()
                .OrderBy(num => num)
                .ToList();
        }

        if (this.NotReadyForSignature)
        {
            this.formData.CreatorNum = null;

            if (this.editContext != null)
            {
                var fieldIdentifier = FieldIdentifier.Create(() => this.formData.CreatorNum);

                this.editContext.MarkAsUnmodified(fieldIdentifier);
            }
        }

        if (this.editContext != null && this.messageStore != null)
        {
            // Clear previous class-level errors before re-validating
            var classFieldIdentifier = new FieldIdentifier(this.formData, string.Empty);
            this.messageStore.Clear(classFieldIdentifier);

            // Only check for model-line match if both model & line are presetn
            if (!string.IsNullOrWhiteSpace(this.formData.Model) &&
                !string.IsNullOrWhiteSpace(this.formData.Line))
            {
                // Validate the entire object container's properties into a temporary list
                var validationResults = new List<ValidationResult>();
                var validationContext = new ValidationContext(this.formData, this.ServiceProvider, null);

                // validateAllProperties: true tells it to run all property-level validation attributes
                Validator.TryValidateObject(this.formData, validationContext, validationResults, validateAllProperties: true);

                // Check if either 'Model' or 'Line' has any property-level errors logged against them
                bool isModelValid = !validationResults.Any(r => r.MemberNames.Contains(nameof(SampleFormData.Model)));
                bool isLineValid = !validationResults.Any(r => r.MemberNames.Contains(nameof(SampleFormData.Line)));

                // Model & line must be individually valid to even merit the cross-check
                if (isModelValid && isLineValid)
                {
                    var crossValidator = new ValidateModelLineExistsAttribute();
                    var crossContext = new ValidationContext(this.formData, this.ServiceProvider, null);

                    ValidationResult? result = crossValidator.GetValidationResult(this.formData, crossContext);

                    if (result != ValidationResult.Success && !string.IsNullOrEmpty(result?.ErrorMessage))
                    {
                        this.messageStore.Add(classFieldIdentifier, result.ErrorMessage);
                    }
                }
            }

            // Notify the UI to re-render validation states
            this.editContext.NotifyValidationStateChanged();
        }
    }

    private void ToggleDummySampleSelection(short dummySampleNum, bool isSelected)
    {
        if (isSelected)
        {
            if (!this.selectedDummySampleNums.Contains(dummySampleNum))
            {
                this.selectedDummySampleNums.Add(dummySampleNum);
                this.selectedDummySampleNums = this.selectedDummySampleNums.OrderBy(num => num).ToList();
            }
        }
        else
        {
            this.selectedDummySampleNums.Remove(dummySampleNum);
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
        this.formData.DummySampleNum = 0;
        this.formData.CreatorNum = null;
        this.selectedDummySampleNums.Clear();
        this.errorMessage = null;
    }

    /// <summary>
    /// Attempts to run the stored procedure with the current form input, populating error message as necessary.
    /// </summary>
    /// <returns>A Task representing successful submission.</returns>
    private async Task HandleSubmit()
    {
        this.errorMessage = null; // Ensure any error messages are for this submission

        if (this.selectedDummySampleNums.Count == 0)
        {
            this.ToastService.Notify(new (ToastType.Danger, "Select at least one dummy sample number before submitting."));
            return;
        }

        this.formData.DummySampleNum = this.selectedDummySampleNums[0];

        // Force the EditContext to execute all validators, including class attributes
        if (this.editContext == null || !this.editContext.Validate())
        {
            this.ToastService.Notify(new (ToastType.Danger, "Please fix the validation errors before submitting."));
            return; // Stop execution if class or field validation fails
        }

        try
        {
            using FPSampleDbContext context = await this.DbFactory.CreateDbContextAsync();
            List<int> createdIds = [];

            foreach (short dummySampleNum in this.selectedDummySampleNums.OrderBy(x => x))
            {
                this.formData.DummySampleNum = dummySampleNum;
                int newId = await CreateSampleAndFetchId(context, this.formData);
                createdIds.Add(newId);
            }

            this.justCreatedSample = createdIds.Count == 1
                ? await context.Samples
                    .AsNoTracking()
                    .FirstOrDefaultAsync(s => s.SampleId == createdIds[0])
                : null;

            await this.RefreshData();
            this.CloseForm();
            this.ToastService.Notify(new (ToastType.Success, createdIds.Count == 1
                ? "Sample created successfully!"
                : $"Created {createdIds.Count} samples successfully!"));
        }
        catch (Exception ex)
        {
            this.errorMessage = $"Database Error: {ex.Message}";
            this.ToastService.Notify(new (ToastType.Danger, "Sample creation failed."));
        }
    }

    private async Task HandlePrintJustCreated()
    {
        this.isAwaitingPrint = false;
        if (this.justCreatedSample != null)
        {
            await this.HandlePrint(this.justCreatedSample);
        }

        this.justCreatedSample = null;
    }

    private void DismissPrintPrompt()
    {
        this.isAwaitingPrint = false;
        this.justCreatedSample = null;
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
            ZplCommand cmd = new () { SampleId = sample.SampleId, PrintDpi = this.printDpi };
            ZebraPrintFlow printObject = new (this.InputProvider, this.Reporter);
            Report statusReport = await printObject.ExecuteAsync(cmd);
            if (statusReport.level == ReportLevel.SUCCESS)
            {
                this.ToastService.Notify(new (ToastType.Success, $"Sample {sample.SampleId} sent to printer."));
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
    /// Prints all samples in <see cref="selectedForPrint"/>, batching over one TCP connection.
    /// </summary>
    /// <returns>A Task representing that all print requests have been issued.</returns>
    private async Task HandlePrint()
    {
        this.isPrinting = true;
        this.printCts = new ();
        this.totalFromQueue = this.selectedForPrint.Count;
        HashSet<Sample> failedSamples = []; // Takes up some more space, but cuts a future query

        using TcpClient conn = new ();
        try
        {
            await conn.ConnectAsync(Config.PrinterIp, Config.PrinterPort, this.printCts.Token);

            foreach (Sample sample in this.selectedForPrint)
            {
                // If the printer is mid-print, let it finish the current label before canceling
                this.printCts.Token.ThrowIfCancellationRequested();

                // Create a print request for each sample
                ZplCommand cmd = new () { SampleId = sample.SampleId, PrintDpi = this.printDpi };
                ZebraPrintFlow printObject = new (this.InputProvider, this.Reporter);
                Report statusReport = await printObject.ExecuteAsync(cmd, conn, leaveOpen: true);
                if (statusReport.level == ReportLevel.SUCCESS)
                {
                    this.ToastService.Notify(new (ToastType.Success, $"Sample #{sample.SampleId} sent to printer."));
                    this.printed++;
                }
                else
                {
                    this.ToastService.Notify(new (ToastType.Danger, $"Sample {sample.SampleId}: {statusReport.message}"));
                    failedSamples.Add(sample);
                }

                await Task.Delay(Config.InterPrintDelayMs, this.printCts.Token); // Wait a second between prints to ensure each toast is visible and that printer isn't overloaded
            }

            // By setting selectedForPrint to only the failed IDs, the user can see easily which samples to investigate
            this.selectedForPrint = failedSamples.ToList();

            // If no prints failed, inform the user and exit print mode
            if (this.selectedForPrint.Count == 0)
            {
                this.ToastService.Notify(new (ToastType.Success, $"Successfully printed all {this.printed} samples!"));
                this.printModeEngaged = false;
            }

            // Otherwise, tell the user which prints failed
            else if (this.selectedForPrint.Count == this.totalFromQueue)
            {
                this.ToastService.Notify(new (ToastType.Danger, "Total Failure", "All prints failed."));
            }
            else
            {
                this.ToastService.Notify(new (ToastType.Warning,
                                            $"Printed {this.printed} of {this.printed + failedSamples.Count} samples (unsuccessful prints still selected)",
                                            $"Failed to print samples: {string.Join(", ", failedSamples.Select(s => s.SampleId))}"));
            }
        }

        // Go here when the user cancels a batch print
        catch (OperationCanceledException)
        {
            this.ToastService.Notify(new (ToastType.Warning, $"Print batch cancelled after {this.printed + failedSamples.Count} of {this.totalFromQueue} labels."));
        }

        // Have to handle Socket & IO exceptions here because this component owns the TCP connection
        catch (SocketException e)
        {
            this.ToastService.Notify(new (ToastType.Danger, $"Error connecting to printer: {e.Message}"));
        }
        catch (IOException e)
        {
            this.ToastService.Notify(new (ToastType.Danger, $"Error executing the print command: {e.Message}"));
        }
        catch (InvalidOperationException e)
        {
            this.ToastService.Notify(new (ToastType.Danger, e.Message));
        }
        finally
        {
            if (conn.Connected)
            {
                conn.Close();
            }

            this.printCts.Dispose();
            this.printCts = null;
            this.isPrinting = false;
        }
    }
}
