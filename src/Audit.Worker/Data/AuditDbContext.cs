using Audit.Worker.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Audit.Worker.Data;

public sealed class AuditDbContext(DbContextOptions<AuditDbContext> options) : DbContext(options)
{
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AuditLog>(entity =>
        {
            entity.ToTable("audit_logs", table =>
            {
                table.HasCheckConstraint("ck_audit_logs_schema_version", "schema_version > 0");
                table.HasCheckConstraint("ck_audit_logs_capacity", "capacity >= 0");
                table.HasCheckConstraint("ck_audit_logs_registered_count", "registered_count >= 0");
                table.HasCheckConstraint("ck_audit_logs_remaining_seats", "remaining_seats = capacity - registered_count");
                table.HasCheckConstraint("ck_audit_logs_version", "version >= 0");
            });
            entity.HasKey(log => log.EventId);
            entity.Property(log => log.EventType).HasMaxLength(64).IsRequired();
            entity.Property(log => log.CorrelationId).HasMaxLength(128).IsRequired();
            entity.Property(log => log.Status).HasMaxLength(16).IsRequired();
            entity.Property(log => log.OccurredAt).HasColumnType("timestamp with time zone").IsRequired();
            entity.Property(log => log.ReceivedAt).HasColumnType("timestamp with time zone").IsRequired();
            entity.HasIndex(log => new { log.CourseSectionId, log.OccurredAt });
            entity.HasIndex(log => new { log.StudentId, log.OccurredAt });
            entity.HasIndex(log => new { log.EventType, log.OccurredAt });
        });

        modelBuilder.UseSnakeCaseNames();
    }
}
