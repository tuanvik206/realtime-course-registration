using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Registration.Api.Data.Entities;
using Shared.Contracts;

namespace Registration.Api.Data;

public sealed class IdentityDbSeeder(IdentityDbContext dbContext, IPasswordHasher<AppUser> passwordHasher)
{
    public async Task SeedAsync(
        string adminPassword,
        string studentPassword,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(adminPassword);
        ArgumentException.ThrowIfNullOrWhiteSpace(studentPassword);

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        await EnsureUserAsync("admin", "DEMO-ADMIN", "Quản trị viên Demo", UserRole.Admin,
            DemoDataIds.AdminUserId, null, adminPassword, cancellationToken);
        await EnsureUserAsync("student1", "DEMO-001", "Sinh viên Demo 1", UserRole.Student,
            DemoDataIds.StudentUser1Id, DemoDataIds.Student1Id, studentPassword, cancellationToken);
        await EnsureUserAsync("student2", "DEMO-002", "Sinh viên Demo 2", UserRole.Student,
            DemoDataIds.StudentUser2Id, DemoDataIds.Student2Id, studentPassword, cancellationToken);
        await EnsureUserAsync("student3", "DEMO-003", "Sinh viên Demo 3", UserRole.Student,
            DemoDataIds.StudentUser3Id, DemoDataIds.Student3Id, studentPassword, cancellationToken);
        await EnsureUserAsync("student4", "DEMO-004", "Sinh viên Demo 4", UserRole.Student,
            DemoDataIds.StudentUser4Id, DemoDataIds.Student4Id, studentPassword, cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task EnsureUserAsync(
        string username,
        string studentCode,
        string fullName,
        UserRole role,
        Guid userId,
        Guid? studentId,
        string password,
        CancellationToken cancellationToken)
    {
        var normalizedUsername = username.ToUpperInvariant();
        var user = await dbContext.Users.SingleOrDefaultAsync(
            candidate => candidate.NormalizedUsername == normalizedUsername,
            cancellationToken);

        if (user is null)
        {
            user = new AppUser
            {
                Id = userId,
                Username = username,
                NormalizedUsername = normalizedUsername,
                PasswordHash = string.Empty,
                Role = role,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };
            user.PasswordHash = passwordHasher.HashPassword(user, password);
            dbContext.Users.Add(user);
        }
        else if (user.Role != role)
        {
            throw new InvalidOperationException($"Seeded account '{username}' has a conflicting role.");
        }

        if (studentId is not null)
        {
            var studentExists = await dbContext.Students.AnyAsync(
                candidate => candidate.Id == studentId.Value || candidate.UserId == user.Id,
                cancellationToken);
            if (!studentExists)
            {
                dbContext.Students.Add(new StudentProfile
                {
                    Id = studentId.Value,
                    UserId = user.Id,
                    StudentCode = studentCode,
                    FullName = fullName,
                    CreatedAt = DateTime.UtcNow
                });
            }
        }
    }
}
