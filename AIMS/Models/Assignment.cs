namespace AIMS.Models;

public enum AssetKind { Hardware = 1, Software = 2 }

public class Assignment
{
    public int AssignmentID { get; set; }
    // Who
    public int UserID { get; set; }
    public User User { get; set; } = null!;

    // What (one of these must be set, enforced in code/migration)
    public AssetKind AssetKind { get; set; }
    public int? AssetTag { get; set; }      // when Hardware
    public Hardware? Hardware { get; set; }
    public int? SoftwareID { get; set; }    // when Software
    public Software? Software { get; set; }

    // When
    public DateTime AssignedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? UnassignedAtUtc { get; set; } // null == active

    // convenience
    public bool IsActive => UnassignedAtUtc == null;
}
