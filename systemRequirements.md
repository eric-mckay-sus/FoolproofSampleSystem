# Foolproof (FP) Sample System

## Five phases

1. [Data loading](#data-loading)
2. [Sample creation](#sample-creation)
3. [Label printing](#label-printing)
4. [Sample approval](#sample-approval)
5. [Sample remake](#sample-remake)

### Data loading

- Tool for uploading FP sample sheets
  - Sheet-wide data
    - Base Model/Product (manually entered to match C. Core, no FK because we want it to be non-retroactive)
    - Revision
    - Issue date
    - Who issued
  - Item-specific data
    - Process failure mode
    - Severity rank (how bad failure is)
    - Location (which machine in the line)
    - Part master number (which physical item used for test)

- Tool for uploading model-line mappings
  - Use C.Core export for now
  - Target generic CSVs of that format to future-proof
  - Match C. Core datatypes & width to guarantee no overflow
    - ICS number (uniquely identifies a model)
    - Short description
    - Production cell code (uniquely identifies a building)
    - Work center code (uniquely identifies a line)
    - Full description

### Sample creation

- Historical database
- Identify model, line, dummy sample number (first two are interchangeable)
- Autofill matched model/line (depending on which is first) from ModelToLine
- Assign 'revision' (iteration, not connected to FP rev) unique across samples for matching model, line, dummy sample number
- Lookup remaining sample data (rank, process failure mode, location) from FP table
- Assign date of creation, approval, and expiration
- Creator self-identifies, must be in AssociateInfo
- Track which sample is active across iterations (new samples are inactive)
- Consider writing as stored procedure

### Label printing

Use TCP for both upload and print

- Upload tool
  - Utility to save a new template to the printer (at specific path)
  - Restrict to ZPL files saving to the R or E drive
  - Require DF command to avoid accidental label print

- Print tool
  - Associate identifies sample to prepare from sample table/specifies sample ID & program can gather the data necessary for print
    - Can select multiple labels to print in batch
  - Choose a template file stored on the printer and send over the field data with the command to recall format (don't send over the template info at print time)

### Sample approval

Literal approval does not touch the DB (in-person, approver must physically sign label)

- Later, approver documents a sample as approved using new SP via form
  - New SP (AuthorizeSample) sets isActive bit and assigns approval info
- Blazor page is a view of Samples filtering out entries without approval info (can't use isActive bit bc that includes outdated)
- Track approver number in DB instead of name

### Sample remake

Sometimes a sample may be lost, break, or otherwise fail to fulfill its test, so it must be remade. This page needs an associate and approver version.

- New table for remake requests
  - Sample ID
  - Dummy sample number
  - Associate name
  - Remake reason
  - Request datetime
  - isClosed bit
- Add column to Samples to track remake date (to hide)

- Associate
  - Show view of samples where isActive is set
  - Provide line/model filters
  - Select a sample, then fill out a remake request form

- Approver
  - Show all remake requests with isClosed bit unset
  - Only action per row is to close (set isClosed)
  - Approver decides whether to actually remake, no built-in remake button (create new sample as usual)
