using AIMS.Models;
using Microsoft.EntityFrameworkCore;

namespace AIMS.Data
{
    public class AimsDbContext : DbContext
    {
        public AimsDbContext(DbContextOptions<AimsDbContext> options) : base(options) { }

        // --- Tables ---
        public DbSet<User> Users { get; set; }
        public DbSet<Role> Roles { get; set; }
        public DbSet<Hardware> HardwareAssets { get; set; }
        public DbSet<Software> SoftwareAssets { get; set; }
        // public DbSet<Feedback> FeedbackEntries { get; set; } # Scaffolded
        public DbSet<AuditLog> AuditLogs { get; set; }
        public DbSet<Assignment> Assignments { get; set; }
        public DbSet<Report> Reports { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.HasDefaultSchema("dbo");
            modelBuilder.Entity<Role>().ToTable("Roles");
            modelBuilder.Entity<User>().ToTable("Users");
            modelBuilder.Entity<Hardware>().ToTable("HardwareAssets");
            modelBuilder.Entity<Software>().ToTable("SoftwareAssets");
            modelBuilder.Entity<AuditLog>().ToTable("AuditLogs");
            // modelBuilder.Entity<Feedback>().ToTable("FeedbackEntries"); # Scaffolded
            modelBuilder.Entity<Assignment>().ToTable("Assignments");
            modelBuilder.Entity<Report>().ToTable("Reports");

            // -------------------------
            // USER RELATIONSHIPS
            // -------------------------

            // User <-> Role (many-to-one)
            modelBuilder.Entity<User>()
                .HasOne(u => u.Role)
                .WithMany(r => r.Users)
                .HasForeignKey(u => u.RoleID)
                // Restrict: don't allow deleting a Role if Users still reference it.
                .OnDelete(DeleteBehavior.Restrict);

            // User <-> Supervisor (self-ref: one supervisor, many direct reports)
            modelBuilder.Entity<User>()
                .HasOne(u => u.Supervisor)
                .WithMany(s => s.DirectReports)
                .HasForeignKey(u => u.SupervisorID)
                // Restrict: you can't delete a supervisor that still has reports.
                .OnDelete(DeleteBehavior.Restrict);

            // -------------------------
            // HARDWARE
            // -------------------------

            // Unique serial number
            modelBuilder.Entity<Hardware>()
                .HasIndex(h => h.SerialNumber)
                .IsUnique();

            // -------------------------
            // SOFTWARE
            // -------------------------

            // Unique license key
            modelBuilder.Entity<Software>()
                .HasIndex(s => s.SoftwareLicenseKey)
                .IsUnique();

            // Money precision (SQL: decimal(10,2))
            modelBuilder.Entity<Software>()
                .Property(s => s.SoftwareCost)
                .HasColumnType("decimal(10,2)");

            // -------------------------
            // FEEDBACK
            // -------------------------

            /* Scaffolded 

            // Feedback ↔ User (many feedback per user)
            modelBuilder.Entity<Feedback>()
                .HasOne(f => f.SubmittedByUser)
                .WithMany(u => u.FeedbackSubmissions)
                .HasForeignKey(f => f.SubmittedByUserID)
                .OnDelete(DeleteBehavior.Cascade);

            */

            // -------------------------
            // AUDIT LOG
            // -------------------------

            // AuditLog -> User (required)
            modelBuilder.Entity<AuditLog>()
                .HasOne(a => a.User)
                .WithMany(u => u.AuditActions)
                .HasForeignKey(a => a.UserID)
                // Restrict: preserve audit integrity; you can't delete a user who has audit rows.
                .OnDelete(DeleteBehavior.Restrict);

            // AuditLog -> Hardware (optional link)
            modelBuilder.Entity<AuditLog>()
                .HasOne(a => a.HardwareAsset)
                .WithMany()
                .HasForeignKey(a => a.AssetTag)
                // NoAction: database will block deleting a hardware row that's referenced by logs.
                // This protects audit trails from dangling references.
                // If we want to allow asset deletion but keep the log, we should use SetNull and keep AssetTag nullable.
                .OnDelete(DeleteBehavior.NoAction);

            // AuditLog -> Software (optional link)
            modelBuilder.Entity<AuditLog>()
                .HasOne(a => a.SoftwareAsset)
                .WithMany()
                .HasForeignKey(a => a.SoftwareID)
                // NoAction: same reasoning as hardware: prevents deleting a software row while logs still point at it.
                .OnDelete(DeleteBehavior.NoAction);

            // XOR constraint for AuditLog target asset (match AssetKind)
            modelBuilder.Entity<AuditLog>()
                .ToTable(tb => tb.HasCheckConstraint(
                    "CK_AuditLog_ExactlyOneAsset",
                    @"
                    (
                        ([AssetKind] = 1 AND [AssetTag] IS NOT NULL AND [SoftwareID] IS NULL)
                        OR
                        ([AssetKind] = 2 AND [SoftwareID] IS NOT NULL AND [AssetTag] IS NULL)
                    )"
                ));

            // -------------------------
            // ASSIGNMENTS
            // -------------------------

            // Assignment -> User
            modelBuilder.Entity<Assignment>()
                .HasOne(a => a.User)
                .WithMany(u => u.Assignments)
                .HasForeignKey(a => a.UserID)
                .OnDelete(DeleteBehavior.Restrict);

            // Assignment -> Hardware (optional)
            modelBuilder.Entity<Assignment>()
                .HasOne(a => a.Hardware)
                .WithMany()
                .HasForeignKey(a => a.AssetTag)
                .OnDelete(DeleteBehavior.Cascade);

            // Assignment -> Software (optional)
            modelBuilder.Entity<Assignment>()
                .HasOne(a => a.Software)
                .WithMany()
                .HasForeignKey(a => a.SoftwareID)
                .OnDelete(DeleteBehavior.Cascade);

            // At most ONE active assignment per hardware asset
            modelBuilder.Entity<Assignment>()
                .HasIndex(a => new { a.AssetTag, a.UnassignedAtUtc })
                .HasFilter("[AssetTag] IS NOT NULL AND [UnassignedAtUtc] IS NULL")
                .IsUnique();

            // At most ONE active assignment per software asset
            modelBuilder.Entity<Assignment>()
                .HasIndex(a => new { a.SoftwareID, a.UnassignedAtUtc })
                .HasFilter("[SoftwareID] IS NOT NULL AND [UnassignedAtUtc] IS NULL")
                .IsUnique();

            // XOR constraint: exactly one of (AssetTag, SoftwareID) must be set
            // AND AssetKind must match.
            modelBuilder.Entity<Assignment>()
                .ToTable(tb => tb.HasCheckConstraint(
                    "CK_Assignment_ExactlyOneAsset",
                    @"
                    (
                        ([AssetKind] = 1 AND [AssetTag] IS NOT NULL AND [SoftwareID] IS NULL)
                        OR
                        ([AssetKind] = 2 AND [SoftwareID] IS NOT NULL AND [AssetTag] IS NULL)
                    )"
                ));

            // -------------------------
            // deterministic IDs / uniqueness helpers
            // -------------------------
            modelBuilder.Entity<User>().HasIndex(u => u.ExternalId).IsUnique();
            modelBuilder.Entity<AuditLog>().HasIndex(a => a.ExternalId).IsUnique();
            modelBuilder.Entity<Report>().HasIndex(r => r.ExternalId).IsUnique();
            modelBuilder.Entity<AuditLog>().Property(a => a.ExternalId).HasDefaultValueSql("NEWID()");
            modelBuilder.Entity<Report>().Property(r => r.ExternalId).HasDefaultValueSql("NEWID()");
        }
    }
}
