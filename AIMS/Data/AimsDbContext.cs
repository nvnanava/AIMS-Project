using AIMS.Models;
using Microsoft.EntityFrameworkCore;

namespace AIMS.Data
{
    public class AimsDbContext : DbContext
    {
        public AimsDbContext(DbContextOptions<AimsDbContext> options) : base(options) { }

        // --- Tables ---
        public DbSet<User> Users { get; set; } = null!;
        public DbSet<Role> Roles { get; set; } = null!;

        public DbSet<Hardware> HardwareAssets { get; set; } = null!;
        public DbSet<Software> SoftwareAssets { get; set; } = null!;

        public DbSet<Assignment> Assignments { get; set; } = null!;

        public DbSet<AuditLog> AuditLogs { get; set; } = null!;
        public DbSet<AuditLogChange> AuditLogChanges { get; set; } = null!;   // child rows

        public DbSet<Report> Reports { get; set; } = null!;

        // Blob-backed payloads: only URIs live in DB; files live in blob storage
        public DbSet<Agreement> Agreements { get; set; } = null!;

        // Optional/aux tables
        public DbSet<Office> Offices { get; set; } = null!;
        public DbSet<Threshold> Thresholds { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // -------------------------
            // Schema & table names
            // -------------------------
            modelBuilder.HasDefaultSchema("dbo");

            modelBuilder.Entity<Role>().ToTable("Roles");
            modelBuilder.Entity<User>().ToTable("Users");

            modelBuilder.Entity<Hardware>().ToTable("HardwareAssets");
            modelBuilder.Entity<Software>().ToTable("SoftwareAssets");

            modelBuilder.Entity<Assignment>().ToTable("Assignments");

            modelBuilder.Entity<AuditLog>().ToTable("AuditLogs");
            modelBuilder.Entity<AuditLogChange>().ToTable("AuditLogChanges");

            modelBuilder.Entity<Report>().ToTable("Reports");
            modelBuilder.Entity<Agreement>().ToTable("Agreements");   // Blob-backed (Agreement.FileUri)

            modelBuilder.Entity<Office>().ToTable("Offices");
            modelBuilder.Entity<Threshold>().ToTable("Thresholds");

            // -------------------------
            // USERS
            // -------------------------
            modelBuilder.Entity<User>()
                .HasOne(u => u.Role)
                .WithMany(r => r.Users)
                .HasForeignKey(u => u.RoleID)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<User>()
                .HasOne(u => u.Supervisor)
                .WithMany(s => s.DirectReports)
                .HasForeignKey(u => u.SupervisorID)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<User>()
                .HasOne(u => u.Office)
                .WithMany(o => o.Users)
                .HasForeignKey(u => u.OfficeID)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<User>()
                .HasIndex(u => u.ExternalId)
                .IsUnique();

            // Azure AD Object ID (GraphObjectID) length
            modelBuilder.Entity<User>()
                .Property(u => u.GraphObjectID)
                .HasMaxLength(64) // forces nvarchar(64) in SQL so it can be indexed
                .IsRequired(); // Every user must have a GraphObjectID

            modelBuilder.Entity<User>()
                .HasIndex(u => u.GraphObjectID)
                .IsUnique(); //prevents duplicate (two rows pointing to same AAD user)
                             //No filter added - every user must have a GraphObjectID and come from AAD
                             //This allows us to only add users that exist in AAD, if not in AAD you cannot add them to our system

            // -------------------------
            // HARDWARE
            // -------------------------
            // NOTE: We no longer enforce uniqueness on AssetTag.
            // If the Hardware model still contains AssetTag, keep only a max length (no required + no unique).
            modelBuilder.Entity<Hardware>()
                .Property(h => h.AssetTag)
                .HasMaxLength(16);

            // Use SerialNumber as the unique, human-visible identifier (when present).
            // Filtered unique index allows multiple NULLs.
            modelBuilder.Entity<Hardware>()
                .Property(h => h.SerialNumber)
                .HasMaxLength(128);

            modelBuilder.Entity<Hardware>()
                .HasIndex(h => h.SerialNumber)
                .IsUnique()
                .HasFilter("[SerialNumber] IS NOT NULL");

            // Common lengths (optional)
            modelBuilder.Entity<Hardware>().Property(h => h.AssetName).HasMaxLength(128);
            modelBuilder.Entity<Hardware>().Property(h => h.AssetType).HasMaxLength(32);
            modelBuilder.Entity<Hardware>().Property(h => h.Status).HasMaxLength(32);
            modelBuilder.Entity<Hardware>().Property(h => h.Manufacturer).HasMaxLength(64);
            modelBuilder.Entity<Hardware>().Property(h => h.Model).HasMaxLength(64);

            // -------------------------
            // SOFTWARE
            // -------------------------
            // Unique license key when present (filtered unique index).
            modelBuilder.Entity<Software>()
                .HasIndex(s => s.SoftwareLicenseKey)
                .IsUnique()
                .HasFilter("[SoftwareLicenseKey] IS NOT NULL");

            modelBuilder.Entity<Software>()
                .Property(s => s.SoftwareCost)
                .HasColumnType("decimal(10,2)");

            // (Optional) lengths
            modelBuilder.Entity<Software>().Property(s => s.SoftwareName).HasMaxLength(128);
            modelBuilder.Entity<Software>().Property(s => s.SoftwareType).HasMaxLength(64);
            modelBuilder.Entity<Software>().Property(s => s.SoftwareVersion).HasMaxLength(64);
            modelBuilder.Entity<Software>().Property(s => s.SoftwareLicenseKey).HasMaxLength(128);

            // -------------------------
            // ASSIGNMENTS
            // -------------------------
            modelBuilder.Entity<Assignment>()
                .HasOne(a => a.User)
                .WithMany(u => u.Assignments)
                .HasForeignKey(a => a.UserID)
                .OnDelete(DeleteBehavior.Restrict);

            // Assignment -> Hardware (optional by HardwareID)
            modelBuilder.Entity<Assignment>()
                .HasOne(a => a.Hardware)
                .WithMany()
                .HasForeignKey(a => a.HardwareID)
                .OnDelete(DeleteBehavior.Cascade);

            // Assignment -> Software (optional)
            modelBuilder.Entity<Assignment>()
                .HasOne(a => a.Software)
                .WithMany()
                .HasForeignKey(a => a.SoftwareID)
                .OnDelete(DeleteBehavior.Cascade);

            // At most ONE active assignment per hardware asset
            modelBuilder.Entity<Assignment>()
                .HasIndex(a => new { a.HardwareID, a.UnassignedAtUtc })
                .HasFilter("[HardwareID] IS NOT NULL AND [UnassignedAtUtc] IS NULL")
                .IsUnique();

            // At most ONE active assignment per (SoftwareID, UserID) (allows multi-seat)
            modelBuilder.Entity<Assignment>()
                .HasIndex(a => new { a.SoftwareID, a.UserID, a.UnassignedAtUtc })
                .HasFilter("[SoftwareID] IS NOT NULL AND [UserID] IS NOT NULL AND [UnassignedAtUtc] IS NULL")
                .IsUnique();

            // Exactly one of (HardwareID, SoftwareID) must be set and match AssetKind
            modelBuilder.Entity<Assignment>()
                .ToTable(tb => tb.HasCheckConstraint(
                    "CK_Assignment_ExactlyOneAsset",
                    @"
                    (
                        ([AssetKind] = 1 AND [HardwareID] IS NOT NULL AND [SoftwareID] IS NULL)
                        OR
                        ([AssetKind] = 2 AND [SoftwareID] IS NOT NULL AND [HardwareID] IS NULL)
                    )"
                ));

            // -------------------------
            // AUDIT LOGS (event + per-field change rows)
            // -------------------------
            modelBuilder.Entity<AuditLog>()
                .HasOne(a => a.User)
                .WithMany(u => u.AuditActions)
                .HasForeignKey(a => a.UserID)
                .OnDelete(DeleteBehavior.Restrict);

            // Optional links to assets
            modelBuilder.Entity<AuditLog>()
                .HasOne(a => a.HardwareAsset)
                .WithMany()
                .HasForeignKey(a => a.HardwareID)
                .OnDelete(DeleteBehavior.NoAction);

            modelBuilder.Entity<AuditLog>()
                .HasOne(a => a.SoftwareAsset)
                .WithMany()
                .HasForeignKey(a => a.SoftwareID)
                .OnDelete(DeleteBehavior.NoAction);

            // Exactly one target (matches AssetKind)
            modelBuilder.Entity<AuditLog>()
                .ToTable(tb => tb.HasCheckConstraint(
                    "CK_AuditLog_ExactlyOneAsset",
                    @"
                    (
                        ([AssetKind] = 1 AND [HardwareID] IS NOT NULL AND [SoftwareID] IS NULL)
                        OR
                        ([AssetKind] = 2 AND [SoftwareID] IS NOT NULL AND [HardwareID] IS NULL)
                    )"
                ));

            modelBuilder.Entity<AuditLog>()
                .HasIndex(a => a.ExternalId)
                .IsUnique();

            modelBuilder.Entity<AuditLog>()
                .Property(a => a.ExternalId)
                .HasDefaultValueSql("NEWID()");

            // Helpful indexes for paging/filtering
            modelBuilder.Entity<AuditLog>()
                .HasIndex(a => a.TimestampUtc);
            modelBuilder.Entity<AuditLog>()
                .HasIndex(a => new { a.AssetKind, a.HardwareID });
            modelBuilder.Entity<AuditLog>()
                .HasIndex(a => new { a.AssetKind, a.SoftwareID });
            modelBuilder.Entity<AuditLog>()
                .HasIndex(a => new { a.UserID, a.Action });

            // Inline large payloads for AuditLog (varbinary(max)); strings default to nvarchar(max)
            modelBuilder.Entity<AuditLog>()
                .Property(a => a.AttachmentBytes)
                .HasColumnType("varbinary(max)");
            modelBuilder.Entity<AuditLog>()
                .Property(a => a.SnapshotBytes)
                .HasColumnType("varbinary(max)");

            // Child rows (per-field diffs)
            modelBuilder.Entity<AuditLogChange>()
                .HasOne(c => c.AuditLog)
                .WithMany(l => l.Changes)
                .HasForeignKey(c => c.AuditLogID)
                .OnDelete(DeleteBehavior.Cascade);

            // No max length limits on field names for flexibility
            // (strings default to nvarchar(max))
            // modelBuilder.Entity<AuditLogChange>()
            //     .Property(c => c.Field)
            //     .HasMaxLength(128);   // ← removed (no max length)

            modelBuilder.Entity<AuditLogChange>()
                .HasIndex(c => new { c.AuditLogID, c.Field });

            // -------------------------
            // REPORTS  Stored as VARCHAR(MAX)
            // -------------------------
            modelBuilder.Entity<Report>()
                .HasIndex(r => r.ExternalId)
                .IsUnique();

            modelBuilder.Entity<Report>()
                .Property(r => r.ExternalId)
                .HasDefaultValueSql("NEWID()");

            modelBuilder.Entity<Report>()
                .HasOne(r => r.GeneratedByUser)
                .WithMany()
                .HasForeignKey(r => r.GeneratedByUserID)
                .OnDelete(DeleteBehavior.SetNull);


            modelBuilder.Entity<Report>()
                .HasOne(r => r.GeneratedForOffice)
                .WithMany()
                .HasForeignKey(r => r.GeneratedForOfficeID)
                .OnDelete(DeleteBehavior.SetNull);

            // -------------------------
            // AGREEMENTS (blob-backed)
            // -------------------------
            modelBuilder.Entity<Agreement>()
                .HasOne(a => a.Hardware)
                .WithMany()
                .HasForeignKey(a => a.HardwareID)
                .OnDelete(DeleteBehavior.NoAction);

            modelBuilder.Entity<Agreement>()
                .HasOne(a => a.Software)
                .WithMany()
                .HasForeignKey(a => a.SoftwareID)
                .OnDelete(DeleteBehavior.NoAction);

            modelBuilder.Entity<Agreement>()
                .ToTable(tb => tb.HasCheckConstraint(
                    "CK_Agreement_ExactlyOneAsset",
                    @"
                    (
                        ([AssetKind] = 1 AND [HardwareID] IS NOT NULL AND [SoftwareID] IS NULL)
                        OR
                        ([AssetKind] = 2 AND [SoftwareID] IS NOT NULL AND [HardwareID] IS NULL)
                    )"
                ));

            // -------------------------
            // OFFICES / THRESHOLDS
            // -------------------------
            modelBuilder.Entity<Office>()
                .HasIndex(o => o.OfficeName);

            modelBuilder.Entity<Threshold>()
                .HasIndex(t => t.AssetType);


            // -------------------------
            // ARCHIVE FILTERS
            // -------------------------
            modelBuilder.Entity<User>().HasQueryFilter(u => !u.IsArchived);
            modelBuilder.Entity<Hardware>().HasQueryFilter(h => !h.IsArchived);
            modelBuilder.Entity<Software>().HasQueryFilter(s => !s.IsArchived);
        }
    }
}
