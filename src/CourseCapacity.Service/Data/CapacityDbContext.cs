using CourseCapacity.Service.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace CourseCapacity.Service.Data;

public sealed class CapacityDbContext(DbContextOptions<CapacityDbContext> options) : DbContext(options)
{
    public DbSet<Subject> Subjects => Set<Subject>();
    public DbSet<Semester> Semesters => Set<Semester>();
    public DbSet<CourseSection> CourseSections => Set<CourseSection>();
    public DbSet<CourseSchedule> CourseSchedules => Set<CourseSchedule>();
    public DbSet<Registration> Registrations => Set<Registration>();
    public DbSet<ProcessedOperation> ProcessedOperations => Set<ProcessedOperation>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Subject>(entity =>
        {
            entity.ToTable("subjects", table => table.HasCheckConstraint("ck_subjects_credits_positive", "credits > 0"));
            entity.HasKey(subject => subject.Id);
            entity.Property(subject => subject.Code).HasMaxLength(32).IsRequired();
            entity.Property(subject => subject.Name).HasMaxLength(160).IsRequired();
            entity.Property(subject => subject.Version).HasDefaultValue(1L).IsRequired();
            entity.HasIndex(subject => subject.Code).IsUnique().HasDatabaseName("ux_subjects_code");
        });

        modelBuilder.Entity<Semester>(entity =>
        {
            entity.ToTable("semesters", table => table.HasCheckConstraint(
                "ck_semesters_date_range",
                "start_date <= end_date"));
            entity.HasKey(semester => semester.Id);
            entity.Property(semester => semester.Code).HasMaxLength(32).IsRequired();
            entity.Property(semester => semester.Name).HasMaxLength(128).IsRequired();
            entity.HasIndex(semester => semester.Code).IsUnique().HasDatabaseName("ux_semesters_code");
        });

        modelBuilder.Entity<CourseSection>(entity =>
        {
            entity.ToTable("course_sections", table =>
            {
                table.HasCheckConstraint("ck_course_sections_capacity_positive", "capacity > 0");
                table.HasCheckConstraint(
                    "ck_course_sections_registered_count",
                    "registered_count >= 0 AND registered_count <= capacity");
                table.HasCheckConstraint("ck_course_sections_version_nonnegative", "version >= 0");
                table.HasCheckConstraint("ck_course_sections_status", "status IN ('Open', 'Closed')");
            });
            entity.HasKey(section => section.Id);
            entity.Property(section => section.CourseCode).HasMaxLength(48).IsRequired();
            entity.Property(section => section.LecturerName).HasMaxLength(160).IsRequired();
            entity.Property(section => section.Status).HasConversion<string>().HasMaxLength(16).IsRequired();
            entity.Property(section => section.CreatedAt).HasColumnType("timestamp with time zone").IsRequired();
            entity.Property(section => section.UpdatedAt).HasColumnType("timestamp with time zone").IsRequired();
            entity.HasIndex(section => new { section.SemesterId, section.CourseCode })
                .IsUnique().HasDatabaseName("ux_course_sections_semester_course_code");
            entity.HasOne(section => section.Subject).WithMany(subject => subject.CourseSections)
                .HasForeignKey(section => section.SubjectId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(section => section.Semester).WithMany(semester => semester.CourseSections)
                .HasForeignKey(section => section.SemesterId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<CourseSchedule>(entity =>
        {
            entity.ToTable("course_schedules", table =>
            {
                table.HasCheckConstraint("ck_course_schedules_weekday", "day_of_week BETWEEN 1 AND 7");
                table.HasCheckConstraint("ck_course_schedules_time_range", "start_time < end_time");
            });
            entity.HasKey(schedule => schedule.Id);
            entity.Property(schedule => schedule.Room).HasMaxLength(64).IsRequired();
            entity.HasIndex(schedule => new
                {
                    schedule.CourseSectionId,
                    schedule.DayOfWeek,
                    schedule.StartTime,
                    schedule.EndTime,
                    schedule.Room
                })
                .IsUnique().HasDatabaseName("ux_course_schedules_slot");
            entity.HasOne(schedule => schedule.CourseSection).WithMany(section => section.Schedules)
                .HasForeignKey(schedule => schedule.CourseSectionId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Registration>(entity =>
        {
            entity.ToTable("registrations", table =>
            {
                table.HasCheckConstraint("ck_registrations_status", "status IN ('Active', 'Cancelled')");
                table.HasCheckConstraint(
                    "ck_registrations_cancelled_at",
                    "(status = 'Active' AND cancelled_at IS NULL) OR (status = 'Cancelled' AND cancelled_at IS NOT NULL)");
            });
            entity.HasKey(registration => registration.Id);
            entity.Property(registration => registration.Status).HasConversion<string>().HasMaxLength(16).IsRequired();
            entity.Property(registration => registration.CreatedAt).HasColumnType("timestamp with time zone").IsRequired();
            entity.Property(registration => registration.CancelledAt).HasColumnType("timestamp with time zone");
            entity.HasIndex(registration => new { registration.StudentId, registration.CourseSectionId })
                .IsUnique()
                .HasFilter("status = 'Active'")
                .HasDatabaseName("ux_registrations_active_student_section");
            entity.HasIndex(registration => new { registration.StudentId, registration.Status });
            entity.HasIndex(registration => new { registration.CourseSectionId, registration.Status });
            entity.HasOne(registration => registration.CourseSection).WithMany(section => section.Registrations)
                .HasForeignKey(registration => registration.CourseSectionId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ProcessedOperation>(entity =>
        {
            entity.ToTable("processed_operations");
            entity.HasKey(operation => new { operation.ActorId, operation.OperationId });
            entity.Property(operation => operation.OperationType).HasMaxLength(48).IsRequired();
            entity.Property(operation => operation.RequestHash).HasMaxLength(128).IsRequired();
            entity.Property(operation => operation.OutcomeCode).HasMaxLength(48).IsRequired();
            entity.Property(operation => operation.ResponseJson).HasColumnType("jsonb").IsRequired();
            entity.Property(operation => operation.CreatedAt).HasColumnType("timestamp with time zone").IsRequired();
        });

        modelBuilder.Entity<OutboxMessage>(entity =>
        {
            entity.ToTable("outbox_messages", table => table.HasCheckConstraint("ck_outbox_version_nonnegative", "version >= 0"));
            entity.HasKey(message => message.EventId);
            entity.Property(message => message.EventType).HasMaxLength(64).IsRequired();
            entity.Property(message => message.PayloadJson).HasColumnType("jsonb").IsRequired();
            entity.Property(message => message.OccurredAt).HasColumnType("timestamp with time zone").IsRequired();
            entity.Property(message => message.PublishedAt).HasColumnType("timestamp with time zone");
            entity.HasIndex(message => new { message.PublishedAt, message.OccurredAt, message.EventId })
                .HasFilter("published_at IS NULL");
        });

        modelBuilder.UseSnakeCaseNames();
    }
}
