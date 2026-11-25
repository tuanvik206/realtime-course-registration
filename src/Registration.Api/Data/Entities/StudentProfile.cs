namespace Registration.Api.Data.Entities;

public sealed class StudentProfile
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public required string StudentCode { get; set; }
    public required string FullName { get; set; }
    public string? Email { get; set; }
    public string? Major { get; set; }
    public DateTime CreatedAt { get; set; }
    public AppUser User { get; set; } = null!;
}
