namespace ECommerce.Application.Features.Auth.DTOs;

public sealed record AuthResponse(string AccessToken, string RefreshToken, int ExpiresIn);

public sealed record UserResponse(Guid Id, string Email, string FirstName, string LastName, List<string> Roles);

public sealed record RegisterRequest(string Email, string Password, string FirstName, string LastName);

public sealed record LoginRequest(string Email, string Password);

public sealed record RefreshTokenRequest(string RefreshToken);

public sealed record RevokeTokenRequest(string RefreshToken);
