using Backend.Data;
using Backend.Dtos;
using Backend.Exceptions;
using Backend.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Backend.Services;

public class AuthService(AppDbContext db, JwtTokenService jwtTokenService)
{
    private static readonly PasswordHasher<User> PasswordHasher = new();

    public async Task<AuthResponse> RegisterAsync(RegisterRequest request)
    {
        var normalizedEmail = request.Email.Trim().ToLowerInvariant();

        if (await db.Users.AnyAsync(u => u.Email == normalizedEmail))
        {
            throw new EmailAlreadyRegisteredException();
        }

        var user = new User
        {
            BusinessName = request.BusinessName.Trim(),
            Email = normalizedEmail,
            PasswordHash = string.Empty
        };
        user.PasswordHash = PasswordHasher.HashPassword(user, request.Password);

        db.Users.Add(user);
        await db.SaveChangesAsync();

        return CreateAuthResponse(user);
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request)
    {
        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        var user = await db.Users.SingleOrDefaultAsync(u => u.Email == normalizedEmail);

        if (user is null || PasswordHasher.VerifyHashedPassword(user, user.PasswordHash, request.Password) == PasswordVerificationResult.Failed)
        {
            throw new InvalidCredentialsException();
        }

        return CreateAuthResponse(user);
    }

    private AuthResponse CreateAuthResponse(User user)
    {
        var (token, expiresAtUtc) = jwtTokenService.CreateToken(user);
        return new AuthResponse(token, expiresAtUtc, user.BusinessName, user.Email);
    }
}
