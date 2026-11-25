namespace Registration.Api.Data.Entities;

public enum UserRole
{
    Student,
    Admin
}

public sealed class AppUser
{
    public Guid Id { get; set; }
    public required string Username { get; set; }
    public required string NormalizedUsername { get; set; }
    public required string PasswordHash { get; set; }
    public UserRole Role { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public StudentProfile? Student { get; set; }
}
