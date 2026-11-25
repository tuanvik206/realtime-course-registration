using CourseCapacity.Service.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Shared.Contracts;

namespace CourseCapacity.Service.Data;

public sealed class CapacityDbSeeder(CapacityDbContext dbContext)
{
    private static readonly DateTime SeededAt = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        if (await dbContext.CourseSections.AnyAsync(cancellationToken))
        {
            return;
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var subjects = new[]
        {
            CreateSubject(1001, "DISTRIBUTED", "Hệ thống phân tán", 3),
            CreateSubject(1002, "DATABASE", "Cơ sở dữ liệu", 3),
            CreateSubject(1003, "WEB", "Phát triển ứng dụng web", 3),
            CreateSubject(1004, "NETWORK", "Mạng máy tính", 3),
            CreateSubject(1005, "ALGORITHM", "Giải thuật", 4),
            CreateSubject(1006, "CLOUD", "Điện toán đám mây", 3)
        };

        var semester = new Semester
        {
            Id = DemoDataIds.SemesterId,
            Code = "2026-FALL",
            Name = "Học kỳ mùa thu 2026",
            StartDate = new DateOnly(2026, 9, 1),
            EndDate = new DateOnly(2027, 1, 15),
            IsActive = true
        };

        var courses = new[]
        {
            CreateCourse(3001, "DISTRIBUTED-01", subjects[0], semester, 2, 1, CourseStatus.Open, 1),
            CreateCourse(3002, "DISTRIBUTED-02", subjects[0], semester, 24, 0, CourseStatus.Open, 2),
            CreateCourse(3003, "DATABASE-01", subjects[1], semester, 30, 0, CourseStatus.Open, 3),
            CreateCourse(3004, "WEB-01", subjects[2], semester, 25, 0, CourseStatus.Open, 4),
            CreateCourse(3005, "NETWORK-01", subjects[3], semester, 20, 0, CourseStatus.Open, 5),
            CreateCourse(3006, "ALGORITHM-01", subjects[4], semester, 30, 0, CourseStatus.Open, 6),
            CreateCourse(3007, "CLOUD-01", subjects[5], semester, 1, 1, CourseStatus.Open, 7),
            CreateCourse(3008, "SEMINAR-01", subjects[0], semester, 20, 0, CourseStatus.Closed, 3)
        };

        dbContext.Subjects.AddRange(subjects);
        dbContext.Semesters.Add(semester);
        dbContext.CourseSections.AddRange(courses);
        dbContext.Registrations.AddRange(
            new Registration
            {
                Id = Guid.Parse("00000000-0000-4000-8000-000000000401"),
                StudentId = DemoDataIds.Student1Id,
                CourseSectionId = DemoDataIds.CourseDistributed1Id,
                Status = RegistrationStatus.Active,
                CreatedAt = SeededAt
            },
            new Registration
            {
                Id = Guid.Parse("00000000-0000-4000-8000-000000000402"),
                StudentId = DemoDataIds.Student2Id,
                CourseSectionId = Guid.Parse("00000000-0000-4000-8000-000000003007"),
                Status = RegistrationStatus.Active,
                CreatedAt = SeededAt
            });

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static Subject CreateSubject(int id, string code, string name, int credits) => new()
    {
        Id = Guid.Parse($"00000000-0000-4000-8000-00000000{id:D4}"),
        Code = code,
        Name = name,
        Credits = credits,
        IsActive = true,
        Version = 1
    };

    private static CourseSection CreateCourse(
        int id,
        string code,
        Subject subject,
        Semester semester,
        int capacity,
        int registeredCount,
        CourseStatus status,
        int dayOfWeek)
    {
        var courseId = id is 3001 or 3002
            ? id == 3001 ? DemoDataIds.CourseDistributed1Id : DemoDataIds.CourseDistributed2Id
            : Guid.Parse($"00000000-0000-4000-8000-00000000{id:D4}");

        var section = new CourseSection
        {
            Id = courseId,
            CourseCode = code,
            SubjectId = subject.Id,
            SemesterId = semester.Id,
            LecturerName = "Giảng viên Demo",
            Capacity = capacity,
            RegisteredCount = registeredCount,
            Status = status,
            Version = 1,
            CreatedAt = SeededAt,
            UpdatedAt = SeededAt
        };

        section.Schedules.Add(new CourseSchedule
        {
            Id = Guid.Parse($"00000000-0000-4000-8000-00000000{id + 5000:D4}"),
            CourseSectionId = courseId,
            DayOfWeek = dayOfWeek,
            StartTime = new TimeOnly(8 + (dayOfWeek % 4), 0),
            EndTime = new TimeOnly(10 + (dayOfWeek % 4), 0),
            Room = $"B{100 + id - 3000}"
        });

        return section;
    }
}
