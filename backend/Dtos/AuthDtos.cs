using System.ComponentModel.DataAnnotations;

namespace Backend.Dtos;

public record RegisterRequest(
    [Required, MaxLength(120)] string BusinessName,
    [Required, EmailAddress, MaxLength(150)] string Email,
    [Required, MinLength(8), MaxLength(100)] string Password
);

public record LoginRequest(
    [Required, EmailAddress] string Email,
    [Required] string Password,
    bool RememberMe
);

public record AuthResponse(string Token, DateTime ExpiresAtUtc, string BusinessName, string Email);
