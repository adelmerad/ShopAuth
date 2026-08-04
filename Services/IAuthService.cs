using AuthApiTest.DTOs;

namespace AuthApiTest.Services;

public interface IAuthService
{
    Task<AuthResponse?> LoginAsync(LoginRequest request);
    Task<AuthResponse?> RefreshTokenAsync(RefreshTokenRequest request);
    Task<bool> LogoutAsync(RefreshTokenRequest request);
    Task<bool> ChangePasswordAsync(string userId, ChangePasswordRequest request);
}