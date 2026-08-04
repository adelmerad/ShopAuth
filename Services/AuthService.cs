using AuthApiTest.Data;
using AuthApiTest.DTOs;
using AuthApiTest.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace AuthApiTest.Services;

public class AuthService : IAuthService
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ITokenService _tokenService;
    private readonly ApplicationDbContext _db;

    public AuthService(
        UserManager<ApplicationUser> userManager,
        ITokenService tokenService,
        ApplicationDbContext db)
    {
        _userManager = userManager;
        _tokenService = tokenService;
        _db = db;
    }

    public async Task<AuthResponse?> LoginAsync(LoginRequest request)
    {
        // 1. L'utilisateur existe-t-il ?
        var user = await _userManager.FindByEmailAsync(request.Email);
        if (user is null)
            return null;

        // 2. Le mot de passe est-il correct ?
        var passwordValid = await _userManager.CheckPasswordAsync(user, request.Password);
        if (!passwordValid)
            return null;

        // 3. Générer le couple de tokens
        return await CreateAuthResponseAsync(user);
    }

    public async Task<AuthResponse?> RefreshTokenAsync(RefreshTokenRequest request)
    {
        // 1. Retrouver le token en base (avec son propriétaire)
        var storedToken = await _db.RefreshTokens
            .Include(rt => rt.User)
            .FirstOrDefaultAsync(rt => rt.Token == request.RefreshToken);

        // 2. Valide ? (existe + pas révoqué + pas expiré)
        if (storedToken is null || storedToken.IsRevoked || storedToken.ExpiresAt < DateTime.UtcNow)
            return null;

        // 3. ROTATION : l'ancien est consommé, on en émet un neuf
        storedToken.IsRevoked = true;
        return await CreateAuthResponseAsync(storedToken.User);
    }

    public async Task<bool> LogoutAsync(RefreshTokenRequest request)
    {
        var storedToken = await _db.RefreshTokens
            .FirstOrDefaultAsync(rt => rt.Token == request.RefreshToken);

        if (storedToken is null)
            return false;

        storedToken.IsRevoked = true;
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> ChangePasswordAsync(string userId, ChangePasswordRequest request)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user is null)
            return false;

        // Vérifie l'ancien mot de passe ET applique le nouveau (avec les règles de complexité)
        var result = await _userManager.ChangePasswordAsync(
            user, request.CurrentPassword, request.NewPassword);

        if (!result.Succeeded)
            return false;

        // Le mot de passe initial a été changé : on lève l'obligation
        user.MustChangePassword = false;
        await _userManager.UpdateAsync(user);
        return true;
    }

    // ------- Méthode privée commune : fabrique la réponse complète -------
    private async Task<AuthResponse> CreateAuthResponseAsync(ApplicationUser user)
    {
        var accessToken = _tokenService.GenerateAccessToken(user);
        var refreshToken = _tokenService.GenerateRefreshToken();

        _db.RefreshTokens.Add(new RefreshToken
        {
            Token = refreshToken,
            ExpiresAt = DateTime.UtcNow.AddDays(7),
            UserId = user.Id
        });
        await _db.SaveChangesAsync();

        return new AuthResponse(accessToken, refreshToken, user.MustChangePassword);
    }
}