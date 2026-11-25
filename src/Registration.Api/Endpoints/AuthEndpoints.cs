using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Registration.Api.Authentication;
using Registration.Api.Data;
using Registration.Api.Data.Entities;
using Registration.Api.Dtos;
using RealtimeCourseRegistration.Contracts.Common;

namespace Registration.Api.Endpoints;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/auth").WithTags("Authentication");
        group.MapPost("/login", LoginAsync).AllowAnonymous().RequireRateLimiting("login");
        group.MapGet("/me", GetMe).RequireAuthorization();
        return endpoints;
    }

    private static async Task<IResult> LoginAsync(
        LoginRequest request,
        HttpContext httpContext,
        IdentityDbContext dbContext,
        IPasswordHasher<AppUser> passwordHasher,
        JwtTokenService tokenService,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password)
            || request.Username.Length > 64 || request.Password.Length > 256)
        {
            return ApiProblemResults.Create(httpContext, 400, ErrorCodes.ValidationError, "Nhập tên đăng nhập và mật khẩu hợp lệ.");
        }

        var normalizedUsername = request.Username.Trim().ToUpperInvariant();
        var user = await dbContext.Users.Include(candidate => candidate.Student)
            .SingleOrDefaultAsync(candidate => candidate.NormalizedUsername == normalizedUsername, cancellationToken);
        if (user is null || !user.IsActive
            || passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.Password)
                == PasswordVerificationResult.Failed)
        {
            return ApiProblemResults.Create(httpContext, 401, ErrorCodes.InvalidCredentials, "Tên đăng nhập hoặc mật khẩu không đúng.");
        }

        var (token, expiresAt) = tokenService.CreateToken(user);
        return Results.Ok(new LoginResponse(token, expiresAt, user.Role.ToString(), user.Student?.Id,
            user.Student?.FullName ?? user.Username));
    }

    private static IResult GetMe(ClaimsPrincipal principal)
    {
        if (!Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            return Results.Unauthorized();
        }
        var studentId = Guid.TryParse(principal.FindFirstValue("student_id"), out var parsedStudentId)
            ? parsedStudentId
            : (Guid?)null;
        return Results.Ok(new UserProfileDto(
            userId,
            principal.FindFirstValue(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.UniqueName)
                ?? principal.Identity?.Name ?? string.Empty,
            principal.FindFirstValue(ClaimTypes.Role) ?? string.Empty,
            studentId,
            principal.FindFirstValue("display_name") ?? string.Empty));
    }
}
