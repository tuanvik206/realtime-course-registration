using CourseCapacity.Service.Data;
using CourseCapacity.Service.Data.Entities;
using CourseCapacity.Service.Services;
using Microsoft.EntityFrameworkCore;
using RealtimeCourseRegistration.Contracts.Capacity.V1;
using Xunit;

namespace IntegrationTests;

public sealed class CapacityConcurrencyTests
{
    [RequiresCapacityTestDatabase]
    [Trait("Category", "Integration")]
    public async Task ParallelReservationsNeverExceedCourseCapacity()
    {
        var connectionString = Environment.GetEnvironmentVariable("CAPACITY_TEST_CONNECTION");
        Assert.False(string.IsNullOrWhiteSpace(connectionString));

        var options = new DbContextOptionsBuilder<CapacityDbContext>().UseNpgsql(connectionString).Options;
        var courseId = Guid.NewGuid();
        var subjectId = Guid.NewGuid();
        var semesterId = Guid.NewGuid();
        var actorIds = Enumerable.Range(0, 102).Select(_ => Guid.NewGuid()).ToArray();
        var studentIds = Enumerable.Range(0, 100).Select(_ => Guid.NewGuid()).ToArray();
        var operationIds = Enumerable.Range(0, 100).Select(_ => Guid.NewGuid()).ToArray();
        const int capacity = 10;

        await using (var setup = new CapacityDbContext(options))
        {
            await setup.Database.MigrateAsync();
            setup.Subjects.Add(new Subject
            {
                Id = subjectId, Code = $"T{Guid.NewGuid():N}"[..12], Name = "Concurrency test subject",
                Credits = 3, IsActive = true, Version = 1
            });
            setup.Semesters.Add(new Semester
            {
                Id = semesterId, Code = $"T{Guid.NewGuid():N}"[..12], Name = "Concurrency test semester",
                StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2027, 1, 1), IsActive = true
            });
            setup.CourseSections.Add(new CourseSection
            {
                Id = courseId, CourseCode = $"T{Guid.NewGuid():N}"[..14], SubjectId = subjectId,
                SemesterId = semesterId, LecturerName = "Integration test", Capacity = capacity,
                RegisteredCount = 0, Status = CourseCapacity.Service.Data.Entities.CourseStatus.Open, Version = 1,
                CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
            });
            await setup.SaveChangesAsync();
        }

        try
        {
            var operations = Enumerable.Range(0, studentIds.Length).Select(async index =>
            {
                await using var context = new CapacityDbContext(options);
                return await new CapacityEnrollmentService(context).ReserveSeatAsync(
                    actorIds[index], studentIds[index], courseId, operationIds[index]);
            });
            var outcomes = await Task.WhenAll(operations);

            Assert.Equal(capacity, outcomes.Count(item => item.Outcome == OperationOutcome.Succeeded));
            Assert.Equal(studentIds.Length - capacity, outcomes.Count(item => item.Outcome == OperationOutcome.CourseFull));
            await using (var retry = new CapacityDbContext(options))
            {
                var repeated = await new CapacityEnrollmentService(retry).ReserveSeatAsync(
                    actorIds[0], studentIds[0], courseId, operationIds[0]);
                Assert.Equal(outcomes[0].Outcome, repeated.Outcome);
            }
            var successfulStudent = studentIds[Array.FindIndex(outcomes, item => item.Outcome == OperationOutcome.Succeeded)];
            var duplicateRequests = Enumerable.Range(100, 2).Select(async index =>
            {
                await using var context = new CapacityDbContext(options);
                return await new CapacityEnrollmentService(context).ReserveSeatAsync(
                    actorIds[index], successfulStudent, courseId, Guid.NewGuid());
            });
            var duplicates = await Task.WhenAll(duplicateRequests);
            Assert.All(duplicates, item => Assert.Equal(OperationOutcome.AlreadyRegistered, item.Outcome));
            await using var verify = new CapacityDbContext(options);
            var section = await verify.CourseSections.AsNoTracking().SingleAsync(item => item.Id == courseId);
            var activeCount = await verify.Registrations.CountAsync(item =>
                item.CourseSectionId == courseId && item.Status == RegistrationStatus.Active);
            var outboxCount = await verify.OutboxMessages.CountAsync(item => item.AggregateId == courseId);
            Assert.Equal(capacity, section.RegisteredCount);
            Assert.Equal(capacity, activeCount);
            Assert.Equal(capacity, outboxCount);
        }
        finally
        {
            await using var cleanup = new CapacityDbContext(options);
            await cleanup.OutboxMessages.Where(item => item.AggregateId == courseId).ExecuteDeleteAsync();
            await cleanup.Registrations.Where(item => item.CourseSectionId == courseId).ExecuteDeleteAsync();
            await cleanup.ProcessedOperations.Where(item => actorIds.Contains(item.ActorId)).ExecuteDeleteAsync();
            await cleanup.CourseSchedules.Where(item => item.CourseSectionId == courseId).ExecuteDeleteAsync();
            await cleanup.CourseSections.Where(item => item.Id == courseId).ExecuteDeleteAsync();
            await cleanup.Subjects.Where(item => item.Id == subjectId).ExecuteDeleteAsync();
            await cleanup.Semesters.Where(item => item.Id == semesterId).ExecuteDeleteAsync();
        }
    }
}

public sealed class RequiresCapacityTestDatabaseAttribute : FactAttribute
{
    public RequiresCapacityTestDatabaseAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CAPACITY_TEST_CONNECTION")))
            Skip = "Set CAPACITY_TEST_CONNECTION to an isolated PostgreSQL test database.";
    }
}
