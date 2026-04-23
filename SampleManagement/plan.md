# Print Integration Plan: Three Increments

## Preliminary Assumptions & Notes

Before diving in, a few things I want to flag:

**On `ExecuteAsync`:** The one-argument overload creates a fresh `TcpConnection` per call, which works fine for single prints but will matter a lot in Increment 3. I'll note this where relevant.

**On `PrintLabel` being a separate project:** It's currently `net9.0-windows10.0.19041` with the Zebra SDK. `SampleManagement` will need a project reference added and will need to target the same minimum Windows version, or you'll want to extract the printer logic into a shared project. I'll call this out in Increment 1 and design around it.

**On `[NotDisplayed]`:** This attribute filters columns in `UniversalTable` via reflection. Applying it to `Sample` properties is clean and non-invasive since `Sample` is already your EF entity — the display concern is clearly separated.

**On the `OnExpand` scaffolding:** `UniversalTable` already supports `OnExpand` and `OnPrint` callbacks per row. `OnPrint` fires the printer icon. The column for both only appears when at least one of them has a delegate, so no structural changes are needed there.

**On printer thread safety:** The Zebra SDK's `TcpConnection` is not thread-safe. Every concurrent `ExecuteAsync` call that opens its own `TcpConnection` risks interleaved bytes on the wire. The plan handles this.

## Increment 1: Single-Sample Print & Column Cleanup

### Goal
Make the table readable at a glance, add the expand flow for detail, and wire up the printer icon to print a single sample.

### 1a — Reshape `Sample` for Display

Apply `[NotDisplayed]` to the columns that are too noisy for a summary view or that belong in the detail panel:

```csharp
// Candidates in Sample:
[NotDisplayed] public int SampleID { get; set; }
[NotDisplayed] public string CreatorName { get; set; }
[NotDisplayed] public string? ApproverName { get; set; }
[NotDisplayed] public DateOnly? ApprovalDate { get; set; }
[NotDisplayed] public DateOnly? ExpirationDate { get; set; }
[NotDisplayed] public DateTime? LastRunTime { get; set; }
[NotDisplayed] public bool IsActive { get; set; }
```

That leaves `DummySampleNum`, `Model`, `Rank`, `Line`, `Iteration`, `CreationDate`, `FailureMode`, and `Location` visible — a meaningful but not overwhelming summary row.

**The approval status gap:** Since `ApprovalDate` is hidden, add a computed display-only DTO or handle it in the expand panel. A lightweight approval badge in the visible row is better than showing the raw date. Since `UniversalTable` works from reflection on `T`, you have two clean options:

- **Option A:** Create a `SampleDisplayRow` DTO with a string `ApprovalStatus` property (e.g. `"✓ Approved"`, `"Pending"`) and swap the table to use it. This avoids touching the EF entity for display concerns.
- **Option B:** Keep `Sample` as the table's `T` and add a non-mapped property with a custom display name via `[Column("Status")]` and compute it from `ApprovalDate != null`.

Option B is simpler and keeps the single-type flow through `TableManager<Sample>`. The non-mapped property won't cause EF issues as long as it has no setter that EF would try to track, or you annotate it with `[NotMapped]`. I'd lean Option B for now since the expand panel is handled separately anyway.

**Assumption:** `[Column]` on a non-mapped property is only used by `UniversalTable`'s reflection for the display name; EF Core won't complain about a getter-only or `[NotMapped]` property. Confirm this holds in your project since `FPSampleDbContext` doesn't configure properties explicitly.

### 1b — The Expand Panel

The `OnExpand` callback already has scaffolding. Wire it in `CreateSample.razor` to set a `Sample? expandedSample` field and reveal a detail card below (or beside) the table. The detail card shows everything hidden from the table: `SampleID`, `CreatorName`, `ApproverName`, `ApprovalDate`, `ExpirationDate`, `LastRunTime`, `IsActive`.

For UX, clicking the expand button on a row that's already expanded should collapse it (toggle). The `UniversalTable` already has `Target` (the highlighted row) and `_attentionItem` (user-clicked focus row) — use `Target` to track the currently expanded sample, since the expand action already sets a relationship between the row and external display. Pass `expandedSample` as `Target` so the row stays highlighted while its detail is shown. Clicking the button again or clicking a different row's expand sets a new `Target`, collapsing the old panel implicitly.

The detail card should sit inside an `expandable-wrapper` that uses your existing CSS accordion pattern, so it slides in/out smoothly. A "Close" button inside the panel sets `expandedSample = null`.

### 1c — Wiring the Printer Icon

The `OnPrint` callback is already scaffolded in `UniversalTable`. In `CreateSample.razor`, bind it to a `HandlePrint(Sample sample)` method.

**The project reference question:** `PrintLabel` currently lives as a standalone executable. You have two paths:

- **Path A:** Add a `<ProjectReference>` from `SampleManagement` to `PrintLabel`. This works if you adjust `PrintLabel.csproj` to also build as a library (change `OutputType` to `Library` or add a second target), and ensure the Windows TFM constraint is acceptable for your deployment target.
- **Path B:** Extract `ExecuteAsync` and the supporting types (`ZplCommand`, `SampleMapFromId`) into a new `PrintLabelCommon` library project (mirroring the `InterProcessIO` pattern already in your solution), and have both `PrintLabel` and `SampleManagement` reference it.

Path B is architecturally cleaner and matches the established pattern in your solution. I'd recommend it. `PrintLabel/Program.cs` becomes a thin CLI wrapper. The printer-facing logic lives in the shared library.

**`HandlePrint` logic:**

```csharp
private async Task HandlePrint(Sample sample)
{
    ZplCommand cmd = new() { IsPrint = true, SampleId = sample.SampleID };
    await PrintLabel.ExecuteAsync(cmd); // or however the shared method is referenced
    ToastService.Notify(new(ToastType.Success, $"Sent sample {sample.SampleID} to printer."));
}
```

Since `ExecuteAsync` is async and touches the printer over TCP, you'll want a loading state on the button to prevent double-clicks. A simple `bool _isPrinting` flag guards this. Show a spinner toast or disable the icon while printing. Catch exceptions from `ConnectionException` and surface them as a danger toast.

**On the upload step:** `ExecuteAsync` with `IsUpload = false` skips the template upload entirely, which is correct for normal operation. The template upload is a one-time setup step via the CLI. Don't expose it in the Blazor UI yet.

### Increment 1 Checklist (before moving on)

- `Sample` reshaped with `[NotDisplayed]` on detail fields
- Approval status badge visible in summary row
- `OnExpand` wired to a smooth detail panel with all hidden fields
- `OnPrint` wired to `HandlePrint` with loading guard and toast feedback
- Printer logic extracted into shared library (or referenced from `PrintLabel`)
- Error handling surfaces `ConnectionException` gracefully

## Increment 2: Multi-Select Print Mode

### Goal
Let users select multiple samples and queue them for sequential printing without blocking the UI during the batch.

### 2a — Print Mode Toggle

Add a "Print Mode" button at the top of `CreateSample.razor`. When pressed, it sets `bool _printModeActive = true` and hides the "Create New Sample" button and the form (the existing `expandable-wrapper` for the form handles this naturally). A second press or an explicit "Exit Print Mode" button resets state.

In print mode, the table's visual behavior changes: rows become selectable checkboxes rather than expand-on-click. The `Target` / expand panel concept should be disabled while in print mode to avoid conflicting interactions.

### 2b — Row Selection in `UniversalTable`

`UniversalTable` currently handles clicks at the row level for `_attentionItem`. For multi-select, you need a controlled selection set, not just one item. Options:

- **Option A:** Add `[Parameter] public HashSet<T>? SelectedItems` and `[Parameter] public EventCallback<HashSet<T>> SelectedItemsChanged` to `UniversalTable`, with a checkbox column that only renders when `SelectedItems` is non-null. This keeps the component generic without breaking existing usages.
- **Option B:** Create a separate `SelectableTable` component that wraps or extends `UniversalTable`.

Option A is less code, more composable, and keeps your single table component. The checkbox column appears only when the parameter is provided, so `FPSheet` and `ModelMappings` are unaffected.

The checkbox maps `item.Equals(...)` using the existing `Equals` override on `Sample` — but `Sample` doesn't override `Equals` yet (only `Associate` does). You'll want to add one keyed on `SampleID`, similar to `Associate`'s implementation.

**Select-all checkbox in the header:** When all visible rows are selected, it should be checked (and clicking it deselects all). When none are selected, clicking it selects all. When some are selected, it's in indeterminate state. This is pure JavaScript state (`indeterminate` property on the checkbox element), which you can set via JS interop or leave as a visual approximation.

### 2c — Staging the Print Queue

When a sample is selected, immediately build a `ZplCommand` and add it to a `List<ZplCommand> _printQueue`:

```csharp
private void HandleSelectionChanged(HashSet<Sample> selected)
{
    _selectedSamples = selected;
    _printQueue = selected
        .Select(s => new ZplCommand { IsPrint = true, SampleId = s.SampleID })
        .ToList();
}
```

This is cheap (no I/O) and means the "Print Selected" button just iterates an already-built collection. Display a badge on the button showing the count of queued items: `Print Selected (N)`.

### 2d — Executing the Queue (Sequential)

Print sequentially, not in parallel. Reason: one physical printer, one TCP port — parallel sends would interleave ZPL commands. The print button handler:

```csharp
private async Task HandlePrintSelected()
{
    _isPrinting = true;
    int printed = 0;
    foreach (ZplCommand cmd in _printQueue)
    {
        try
        {
            await PrintLabel.ExecuteAsync(cmd);
            printed++;
        }
        catch (Exception ex)
        {
            ToastService.Notify(new(ToastType.Danger, $"Print failed for sample {cmd.SampleId}: {ex.Message}"));
        }
    }
    ToastService.Notify(new(ToastType.Success, $"Printed {printed} of {_printQueue.Count} samples."));
    _isPrinting = false;
    _selectedSamples.Clear();
    _printQueue.Clear();
}
```

Each call to `ExecuteAsync` opens its own TCP connection, sends, and closes. This is the safest pattern given the current SDK usage and avoids any connection state bleed between prints.

**UX note:** Show a progress indicator (e.g. `Printing 2 of 5...`) rather than just a spinner, since a batch could take several seconds. Update a `_printProgress` counter inside the loop and call `StateHasChanged()` after each iteration.

### Increment 2 Checklist

- Print mode toggle that hides creation UI and changes table interaction model
- Multi-select checkboxes in `UniversalTable` (conditional on parameter)
- `Sample.Equals` override keyed on `SampleID`
- Real-time print queue built from selection
- Sequential execution with per-item error handling and progress reporting
- Exit print mode clears selection and queue

## Increment 3: Multi-Print Optimizations

### Goal
Reduce total print time for batches by reusing the printer connection across multiple labels, handling printer-side throughput limits, and giving the user feedback if the printer falls behind.

### 3a — Connection Reuse Across a Batch

The current `ExecuteAsync(ZplCommand)` overload opens and closes a TCP connection per call. For a batch of 10 labels, that's 10 handshakes. Instead, expose the two-argument overload `ExecuteAsync(ZplCommand, Connection)` from your shared library and manage a single connection across the batch in Blazor:

```csharp
TcpConnection conn = new(Config.GetPrinterIp(), TcpConnection.DEFAULT_ZPL_TCP_PORT);
conn.Open();
try
{
    foreach (ZplCommand cmd in _printQueue)
    {
        await PrintLabel.ExecuteAsync(cmd, conn);
        // throttle here (see 3b)
    }
}
finally
{
    if (conn.Connected) conn.Close();
}
```

This requires `Config.GetPrinterIp()` to be accessible from `SampleManagement`, which is another argument for the shared library approach from Increment 1.

### 3b — Printer Throughput Throttling

Zebra printers have an internal print buffer. If you send labels faster than the printer can process them (especially if the label format is complex or the printer is slow), you can overflow the buffer and get garbled or dropped prints. The SDK does not provide a built-in acknowledgment mechanism in the `TcpConnection` send path — it's fire-and-forget at the protocol level.

The practical mitigation is a configurable inter-print delay:

```csharp
await PrintLabel.ExecuteAsync(cmd, conn);
await Task.Delay(TimeSpan.FromMilliseconds(Config.InterPrintDelayMs));
```

Expose `InterPrintDelayMs` in your `Config` class (start around 500ms and tune based on observed behavior). This is a simpler and more reliable approach than trying to poll printer status, and it prevents the connection from being overwhelmed.

**If you want status polling:** The Zebra SDK does expose `printer.GetCurrentStatus()` which returns a `PrinterStatus` object. You could poll `PrinterStatus.isReadyToPrint` between sends rather than using a fixed delay:

```csharp
await PrintLabel.ExecuteAsync(cmd, conn);
ZebraPrinter printer = ZebraPrinterFactory.GetInstance(conn);
PrinterStatus status;
do {
    await Task.Delay(100);
    status = printer.GetCurrentStatus();
} while (!status.isReadyToPrint);
```

This is more responsive but adds complexity and a dependency on the status API working reliably over TCP. I'd keep the fixed delay for Increment 3 and revisit polling as a follow-up if batches are too slow.

### 3c — Cancellation Support

A batch of 20+ prints that goes wrong mid-way needs a way to stop. Add a `CancellationTokenSource _printCts` and a "Cancel Print Batch" button that appears during printing:

```csharp
_printCts = new CancellationTokenSource();
foreach (ZplCommand cmd in _printQueue)
{
    _printCts.Token.ThrowIfCancellationRequested();
    await PrintLabel.ExecuteAsync(cmd, conn);
    await Task.Delay(Config.InterPrintDelayMs, _printCts.Token);
}
```

Catch `OperationCanceledException` outside the loop and show a warning toast: `"Print batch canceled after {printed} of {total} labels."` Close the connection in the `finally` block regardless.

### 3d — Upload-Once, Print-Many

Currently the ZPL template must already be on the printer's R drive. Consider adding a one-click "Upload Template" button (admin-only, perhaps in `ApproveSamples` or a settings area) that calls `ExecuteAsync` with `IsUpload = true, IsPrint = false`. This way, if the template is ever lost from printer memory (power cycle, firmware update), it can be restored from the UI without running the CLI tool. This is a small addition but closes a real operational gap.

### Increment 3 Checklist

- Single `TcpConnection` reused across a batch, closed in `finally`
- Configurable inter-print delay to respect printer buffer limits
- `CancellationTokenSource` wired to a "Cancel" button visible during printing
- Template upload accessible from the Blazor UI (admin only)
- `InterPrintDelayMs` exposed in shared `Config`

## Summary of Dependencies Between Increments

Increment 1 must establish the shared library before 2 and 3 can build on it. Increment 2's connection-per-call approach is intentionally simple so it can be validated before Increment 3 optimizes it. The `CancellationTokenSource` pattern in Increment 3 mirrors what `UploadPageBase` already does with `TimerCts`, so that idiom is already familiar in this codebase.

The one thing I'd flag before starting Increment 1: confirm whether `SampleManagement`'s deployment target supports the `net9.0-windows10.0.19041` TFM constraint introduced by the Zebra SDK. If the server runs Linux (which is common for Blazor Server deployments), the Zebra SDK will be a blocker and you'll need to validate that it ships a platform-neutral assembly, or isolate the print calls behind a Windows-only service boundary.
