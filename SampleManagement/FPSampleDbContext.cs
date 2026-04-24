// <copyright file="FPSampleDbContext.cs" company="Stanley Electric US Co. Inc.">
// Copyright (c) 2026 Stanley Electric US Co. Inc. Licensed under the MIT License.
// </copyright>

namespace SampleManagement;

using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations.Schema;

/// <summary>
/// Represents the state of the database in a way friendly to EF Core.
/// </summary>
/// <param name="options">The server details and login credentials.</param>
public class FPSampleDbContext(DbContextOptions<FPSampleDbContext> options) : DbContext(options)
{
    /// <summary>
    /// Gets or sets the foolproof entries table.
    /// </summary>
    public DbSet<FoolproofEntry> FoolproofInfo { get; set; }

    /// <summary>
    /// Gets or sets the model-to-line mapping table.
    /// </summary>
    public DbSet<ModelLine> ModelToLine { get; set; }

    /// <summary>
    /// Gets or sets the samples table.
    /// </summary>
    public DbSet<Sample> Samples { get; set; }

    /// <summary>
    /// Gets or sets the associate information table.
    /// </summary>
    public DbSet<Associate> AssociateInfo { get; set; }
}

/// <summary>
/// Represents a foolproof entry record in the database.
/// </summary>
[PrimaryKey(nameof(Model), nameof(Revision), nameof(Location), nameof(DummySampleNum))]
public class FoolproofEntry
{
    /// <summary>
    /// Gets or sets the model identifier for this foolproof entry.
    /// </summary>
    [Column("model")]
    public required string Model { get; set; }

    /// <summary>
    /// Gets or sets the revision number for this foolproof entry.
    /// </summary>
    [Column("revision")]
    public byte Revision { get; set; }

    /// <summary>
    /// Gets or sets the issue date for this foolproof entry.
    /// </summary>
    [Column("issueDate")]
    public DateOnly IssueDate { get; set; }

    /// <summary>
    /// Gets or sets the issuing associate for this foolproof entry.
    /// </summary>
    [Column("issuer")]
    public string? Issuer { get; set; }

    /// <summary>
    /// Gets or sets the failure mode for this foolproof entry.
    /// </summary>
    [Column("failureMode")]
    public required string FailureMode { get; set; }

    /// <summary>
    /// Gets or sets the rank for this foolproof entry.
    /// </summary>
    [Column("rank")]
    public required string Rank { get; set; }

    /// <summary>
    /// Gets or sets the location for this foolproof entry.
    /// </summary>
    [Column("location")]
    public required string Location { get; set; }

    /// <summary>
    /// Gets or sets the dummy sample number for this foolproof entry.
    /// </summary>
    [Column("dummySampleNum")]
    public short DummySampleNum { get; set; }
}

/// <summary>
/// Represents a model line mapping record in the database.
/// </summary>
[PrimaryKey(nameof(IcsNum), nameof(WorkCenterCode))]
public class ModelLine
{
    /// <summary>
    /// Gets or sets the ICS number for this model line.
    /// </summary>
    [Column("icsNum")]
    public required string IcsNum { get; set; }

    /// <summary>
    /// Gets or sets the short description for this model line.
    /// </summary>
    [Column("shortDesc")]
    public required string ShortDescription { get; set; }

    /// <summary>
    /// Gets or sets the production cell code for this model line.
    /// </summary>
    [Column("prodCellCode")]
    public required string ProdCellCode { get; set; }

    /// <summary>
    /// Gets or sets the work center code for this model line.
    /// </summary>
    [Column("workCenterCode")]
    public required string WorkCenterCode { get; set; }

    /// <summary>
    /// Gets or sets the full description for this model line.
    /// </summary>
    [Column("fullDesc")]
    public required string FullDescription { get; set; }
}

/// <summary>
/// Represents a sample record in the database.
/// </summary>
[PrimaryKey(nameof(SampleID))]
public class Sample
{
    /// <summary>
    /// Gets or sets the unique sample identifier.
    /// </summary>
    [Column("sampleID")]
    public int SampleID { get; set; }

    /// <summary>
    /// Gets or sets the dummy sample number for this sample.
    /// </summary>
    [Column("dummySampleNum")]
    public short DummySampleNum { get; set; }

    /// <summary>
    /// Gets or sets the model name for this sample.
    /// </summary>
    [Column("model")]
    public required string Model { get; set; }

    /// <summary>
    /// Gets or sets the rank for this sample.
    /// </summary>
    [Column("rank", TypeName = "char(1)")]
    public char Rank { get; set; }

    /// <summary>
    /// Gets or sets the line code for this sample.
    /// </summary>
    [Column("workCenterCode")]
    public required string Line { get; set; }

    /// <summary>
    /// Gets or sets the iteration number for this sample.
    /// </summary>
    [Column("iteration")]
    public byte Iteration { get; set; }

    /// <summary>
    /// Gets or sets the creation date for this sample.
    /// </summary>
    [Column("creationDate")]
    public DateOnly CreationDate { get; set; }

    /// <summary>
    /// Gets or sets the failure mode for this sample.
    /// </summary>
    [Column("failureMode")]
    public required string FailureMode { get; set; }

    /// <summary>
    /// Gets or sets the location for this sample.
    /// </summary>
    [Column("location")]
    public required string Location { get; set; }

    /// <summary>
    /// Gets or sets the creator name for this sample.
    /// </summary>
    [Column("creatorName")]
    [Verbose]
    public required string CreatorName { get; set; }

    /// <summary>
    /// Gets or sets the approver name for this sample.
    /// </summary>
    [Column("approverName")]
    [Verbose]
    public string? ApproverName { get; set; }

    /// <summary>
    /// Gets or sets the approval date for this sample.
    /// </summary>
    [Column("approvalDate")]
    [Verbose]
    public DateOnly? ApprovalDate { get; set; }

    /// <summary>
    /// Gets or sets the expiration date for this sample.
    /// </summary>
    [Column("expirationDate")]
    [Verbose]
    public DateOnly? ExpirationDate { get; set; }

    /// <summary>
    /// Gets or sets the last run time for this sample.
    /// </summary>
    [Column("lastRunTime")]
    [Verbose]
    public DateTime? LastRunTime { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether this sample is active.
    /// </summary>
    [Column("isActive")]
    [Verbose]
    public bool IsActive { get; set; }

    /// <summary>
    /// Associates are equal if they share the same badge number.
    /// </summary>
    /// <param name="obj">The object to compare.</param>
    /// <returns>True when the objects represent the same associate.</returns>
    public override bool Equals(object? obj)
    {
        if (obj is Sample other)
        {
            return this.SampleID == other.SampleID;
        }

        return false;
    }

    /// <summary>
    /// Gets the hash code for this associate.
    /// </summary>
    /// <returns>The associate's hash code.</returns>
    public override int GetHashCode() => this.SampleID.GetHashCode();

    /// <summary>
    /// Returns a descriptive string representation of this associate.
    /// </summary>
    /// <returns>The associate description.</returns>
    public override string ToString()
    {
        return $"ID: {this.SampleID}, Sample #: {this.DummySampleNum}, Model: {this.Model}, Line: {this.Line}";
    }
}

/// <summary>
/// Represents an associate record in the database.
/// </summary>
[PrimaryKey(nameof(BadgeNum))]
public class Associate
{
    /// <summary>
    /// Gets or sets the associate badge number.
    /// </summary>
    [Column("badgeNum")]
    public int BadgeNum { get; set; }

    /// <summary>
    /// Gets or sets the associate number.
    /// </summary>
    [Column("associateNum")]
    public int AssociateNum { get; set; }

    /// <summary>
    /// Gets or sets the associate's name.
    /// </summary>
    [Column("associateName")]
    public string? Name { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether this associate is an administrator.
    /// </summary>
    [Column("isAdmin")]
    [NotDisplayed]
    public bool IsAdmin { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether this associate is an authorized FP sample approver.
    /// </summary>
    [Column("isApprover")]
    [NotDisplayed]
    public bool IsApprover { get; set; }

    /// <summary>
    /// Associates are equal if they share the same badge number.
    /// </summary>
    /// <param name="obj">The object to compare.</param>
    /// <returns>True when the objects represent the same associate.</returns>
    public override bool Equals(object? obj)
    {
        if (obj is Associate other)
        {
            return this.BadgeNum == other.BadgeNum;
        }

        return false;
    }

    /// <summary>
    /// Gets the hash code for this associate.
    /// </summary>
    /// <returns>The associate's hash code.</returns>
    public override int GetHashCode() => this.BadgeNum.GetHashCode();

    /// <summary>
    /// Returns a descriptive string representation of this associate.
    /// </summary>
    /// <returns>The associate description.</returns>
    public override string ToString()
    {
        return $"Name: {this.Name}, Assoc #: {this.AssociateNum}, Badge #: {this.BadgeNum}";
    }
}
