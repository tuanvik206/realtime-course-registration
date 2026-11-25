using Microsoft.EntityFrameworkCore;
using Registration.Api.Data.Entities;

namespace Registration.Api.Data;

public sealed class IdentityDbContext(DbContextOptions<IdentityDbContext> options) : DbContext(options)
{
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<StudentProfile> Students => Set<StudentProfile>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AppUser>(entity =>
        {
            entity.ToTable("users", table => table.HasCheckConstraint(
                "ck_users_role",
                "role IN ('Student', 'Admin')"));
            entity.HasKey(user => user.Id);
            entity.Property(user => user.Username).HasMaxLength(64).IsRequired();
            entity.Property(user => user.NormalizedUsername).HasMaxLength(64).IsRequired();
            entity.Property(user => user.PasswordHash).HasMaxLength(512).IsRequired();
            entity.Property(user => user.Role).HasConversion<string>().HasMaxLength(16).IsRequired();
            entity.Property(user => user.IsActive).IsRequired();
            entity.Property(user => user.CreatedAt).HasColumnType("timestamp with time zone").IsRequired();
            entity.HasIndex(user => user.NormalizedUsername).IsUnique()
                .HasDatabaseName("ux_users_normalized_username");
            entity.HasOne(user => user.Student)
                .WithOne(student => student.User)
                .HasForeignKey<StudentProfile>(student => student.UserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<StudentProfile>(entity =>
        {
            entity.ToTable("students");
            entity.HasKey(student => student.Id);
            entity.Property(student => student.StudentCode).HasMaxLength(32).IsRequired();
            entity.Property(student => student.FullName).HasMaxLength(160).IsRequired();
            entity.Property(student => student.Email).HasMaxLength(254);
            entity.Property(student => student.Major).HasMaxLength(128);
            entity.Property(student => student.CreatedAt).HasColumnType("timestamp with time zone").IsRequired();
            entity.HasIndex(student => student.UserId).IsUnique()
                .HasDatabaseName("ux_students_user_id");
            entity.HasIndex(student => student.StudentCode).IsUnique()
                .HasDatabaseName("ux_students_student_code");
        });

        modelBuilder.UseSnakeCaseNames();
    }
}
