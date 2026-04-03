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
    - Base Model/Product (as model)
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

- Associate identifies sample to prepare
- Format for printing
- Approval happens immediately after print

### Sample approval

- Some way to track who is authorized to approve samples

### Sample remake
