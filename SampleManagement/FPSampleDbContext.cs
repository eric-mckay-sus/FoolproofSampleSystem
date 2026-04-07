using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations.Schema;

namespace SampleManagement;
/// <summary>
/// Represents the state of the database in a way friendly to EFCore
/// </summary>
/// <param name="options">The server details and login credentials</param>
public class FPSampleDbContext(DbContextOptions<FPSampleDbContext> options) : DbContext(options)
{
    // One set per table, MUST match table names
    public DbSet<FoolproofEntry> FoolproofInfo { get; set; }
    public DbSet<ModelLine> ModelToLine { get; set; }
    public DbSet<Sample> Samples { get; set; }
}

[PrimaryKey(nameof(Model), nameof(Revision), nameof(Location), nameof(DummySampleNum))]
public class FoolproofEntry
{
    [Column("model")]
    public required string Model { get; set; }

    [Column("revision")]
    public byte Revision { get; set; }

    [Column("issueDate")]
    public DateOnly IssueDate { get; set; }

    [Column("issuer")]
    public string? Issuer { get; set; }

    [Column("failureMode")]
    public required string FailureMode { get; set; }

    [Column("rank")]
    public required string Rank { get; set; }

    [Column("location")]
    public required string Location { get; set; }

    [Column("dummySampleNum")]
    public short DummySampleNum { get; set; }
}

[PrimaryKey(nameof(IcsNum), nameof(WorkCenterCode))]
public class ModelLine
{
    [Column("icsNum")]
    public required string IcsNum { get; set; }

    [Column("shortDesc")]
    public required string ShortDescription { get; set; }

    [Column("prodCellCode")]
    public required string ProdCellCode { get; set; }

    [Column("workCenterCode")]
    public required string WorkCenterCode { get; set; }

    [Column("fullDesc")]
    public required string FullDescription { get; set; }
}

[PrimaryKey(nameof(SampleID))]
public class Sample
{
    [Column("sampleID")]
    public int SampleID { get; set; }

    [Column("dummySampleNum")]
    public short DummySampleNum { get; set; }

    [Column("model")]
    public required string Model { get; set; }

    [Column("rank", TypeName = "char(1)")]
    public char Rank { get; set; }

    [Column("workCenterCode")]
    public required string Line { get; set; }

    [Column("iteration")]
    public byte Iteration { get; set; }

    [Column("creationDate")]
    public DateOnly CreationDate { get; set; }

    [Column("failureMode")]
    public required string FailureMode { get; set; }

    [Column("location")]
    public required string Location { get; set; }

    [Column("creatorName")]
    public required string CreatorName { get; set; }

    [Column("approverName")]
    public string? ApproverName { get; set; }

    [Column("approvalDate")]
    public DateOnly? ApprovalDate { get; set; }

    [Column("expirationDate")]
    public DateOnly? ExpirationDate { get; set; }

    [Column("lastRunTime")]
    public DateTime? LastRunTime { get; set; }

    [Column("isActive")]
    public bool IsActive { get; set; }
}

[PrimaryKey(nameof(BadgeNum))]
public class Associate
{
    [Column("badgeNum")]
    public int BadgeNum { get; set; }

    [Column("associateNum")]
    public int AssociateNum { get; set; }

    [Column("associateName")]
    public string? Name { get; set; }

    [Column("isAdmin")]
    [NotDisplayed]
    public bool IsAdmin { get; set; }

    /// <summary>
    /// Associates are equal if they share the same badge number (by PK definition)
    /// </summary>
    /// <param name="obj"></param>
    /// <returns>Whether this associate equals <paramref="obj /></returns>
    public override bool Equals(object? obj)
    {
        if (obj is Associate other)
        {
            return BadgeNum == other.BadgeNum;
        }
        return false;
    }

    /// <summary>
    /// The hash code of an associate is the hash of its badge number
    /// </summary>
    /// <returns>The associate's hash code</returns>
    public override int GetHashCode() => BadgeNum.GetHashCode();

    public override string ToString()
    {
        return $"Name: {Name}, Assoc #: {AssociateNum}, Badge #: {BadgeNum}";
    }
}
