using Microsoft.EntityFrameworkCore;
using Deduplicator.Data.Models;
using FileModel = Deduplicator.Data.Models.File;

namespace Deduplicator.Data;

public class DeduplicatorContext : DbContext
{
    public DbSet<Container> Containers { get; set; } = null!;
    public DbSet<FileModel> Files { get; set; } = null!;
    public DbSet<ScanSession> ScanSessions { get; set; } = null!;
    public DbSet<FileTask> FileTasks { get; set; } = null!;

    private readonly string _dbPath;

    public DeduplicatorContext(string dbPath)
    {
        _dbPath = dbPath;
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.UseSqlite($"Data Source={_dbPath}");
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Container configuration
        modelBuilder.Entity<Container>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.PartitionGuid).HasDefaultValue("null");
            entity.Property(e => e.DiskId).IsRequired();
            entity.HasIndex(e => new { e.PartitionGuid, e.DiskId }).IsUnique();
        });

        // File configuration
        modelBuilder.Entity<FileModel>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired();
            entity.Property(e => e.Path).IsRequired();
            entity.Property(e => e.MediaType).IsRequired();
            entity.Property(e => e.Size).IsRequired();

            entity.HasIndex(e => new { e.ContainerId, e.Path, e.Name }).IsUnique();
            entity.HasIndex(e => new { e.Size, e.MetadataTimestamp });
            entity.HasIndex(e => e.MediaType);
            entity.HasIndex(e => e.LastScanSessionId);

            entity.HasOne(e => e.Container)
                .WithMany(c => c.Files)
                .HasForeignKey(e => e.ContainerId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.LastScanSession)
                .WithMany(s => s.Files)
                .HasForeignKey(e => e.LastScanSessionId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // ScanSession configuration
        modelBuilder.Entity<ScanSession>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.RootPath).IsRequired();
            entity.Property(e => e.Status).IsRequired().HasDefaultValue("in_progress");
            entity.Property(e => e.StartedAt).IsRequired();
            entity.Property(e => e.FilesProcessed).HasDefaultValue(0);

            entity.HasOne(e => e.Container)
                .WithMany(c => c.ScanSessions)
                .HasForeignKey(e => e.ContainerId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // FileTask configuration
        modelBuilder.Entity<FileTask>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Operation).IsRequired();

            entity.HasOne(e => e.File)
                .WithMany()
                .HasForeignKey(e => e.FileId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(e => e.FileId);
        });
    }

    public void EnsureCreated()
    {
        Database.EnsureCreated();

        // Enable type constraints
        Database.ExecuteSqlRaw("PRAGMA foreign_keys = ON;");

        // Add CHECK constraint for status if not exists
        try
        {
            Database.ExecuteSqlRaw(@"
                CREATE TABLE IF NOT EXISTS scan_session_check AS
                SELECT * FROM ScanSessions WHERE 1=0;

                DROP TABLE IF EXISTS scan_session_check;
            ");
        }
        catch
        {
            // Constraint might already exist
        }
    }
}
