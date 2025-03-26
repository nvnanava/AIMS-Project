using System;
using System.Collections.Generic;
using AIMS.Models;
using Microsoft.EntityFrameworkCore;

namespace AIMS.Data;

public partial class AimsDbContext : DbContext
{
    public AimsDbContext()
    {
    }

    public AimsDbContext(DbContextOptions<AimsDbContext> options)
        : base(options)
    {
    }

    public virtual DbSet<Hardware> Hardwares { get; set; }

    public virtual DbSet<Software> Softwares { get; set; }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
#warning To protect potentially sensitive information in your connection string, you should move it out of source code. You can avoid scaffolding the connection string by using the Name= syntax to read it from configuration - see https://go.microsoft.com/fwlink/?linkid=2131148. For more guidance on storing connection strings, see https://go.microsoft.com/fwlink/?LinkId=723263.
        => optionsBuilder.UseSqlServer("Connection String"); // Replace "Connection String" with your connection string. Will put format in discord.

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Hardware>(entity =>
        {
            entity.HasKey(e => e.AssetTag).HasName("PK__Hardware__89F276AACC2EBD54");

            entity.ToTable("Hardware");

            entity.HasIndex(e => e.SerialNumber, "UQ__Hardware__048A000838FBE506").IsUnique();

            entity.Property(e => e.AssetName).HasMaxLength(255);
            entity.Property(e => e.AssetType).HasMaxLength(255);
            entity.Property(e => e.Manufacturer).HasMaxLength(255);
            entity.Property(e => e.Model).HasMaxLength(255);
            entity.Property(e => e.SerialNumber).HasMaxLength(255);
            entity.Property(e => e.Status).HasMaxLength(255);
        });

        modelBuilder.Entity<Software>(entity =>
        {
            entity.HasKey(e => e.SoftwareId).HasName("PK__Software__25EDB8DC7B909065");

            entity.ToTable("Software");

            entity.HasIndex(e => e.SoftwareLicenseKey, "UQ__Software__953EDCAE2A3AE925").IsUnique();

            entity.Property(e => e.SoftwareId).HasColumnName("SoftwareID");
            entity.Property(e => e.SoftwareCost).HasColumnType("decimal(10, 2)");
            entity.Property(e => e.SoftwareDeploymentLocation).HasMaxLength(255);
            entity.Property(e => e.SoftwareLicenseKey).HasMaxLength(255);
            entity.Property(e => e.SoftwareName).HasMaxLength(255);
            entity.Property(e => e.SoftwareType).HasMaxLength(255);
            entity.Property(e => e.SoftwareVersion).HasMaxLength(255);
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
