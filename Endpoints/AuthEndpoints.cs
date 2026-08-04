using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using AuthApiTest.DTOs;
using AuthApiTest.Services;

namespace AuthApiTest.Endpoints;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/auth");

        group.MapPost("/login", async (LoginRequest request, IAuthService authService) =>
        {
            var response = await authService.LoginAsync(request);
            return response is not null
                ? Results.Ok(response)
                : Results.Unauthorized();
        });

        group.MapPost("/refresh-token", async (RefreshTokenRequest request, IAuthService authService) =>
        {
            var response = await authService.RefreshTokenAsync(request);
            return response is not null
                ? Results.Ok(response)
                : Results.Unauthorized();
        });

        group.MapPost("/logout", async (RefreshTokenRequest request, IAuthService authService) =>
        {
            var success = await authService.LogoutAsync(request);
            return success ? Results.Ok() : Results.NotFound();
        });

        group.MapPost("/change-password",
            async (ChangePasswordRequest request, IAuthService authService, ClaimsPrincipal user) =>
            {
                // Extraire l'id de l'utilisateur DEPUIS SON TOKEN
                var userId = user.FindFirstValue(JwtRegisteredClaimNames.Sub)
                          ?? user.FindFirstValue(ClaimTypes.NameIdentifier);

                if (userId is null)
                    return Results.Unauthorized();

                var success = await authService.ChangePasswordAsync(userId, request);
                return success ? Results.Ok() : Results.BadRequest();
            })
        .RequireAuthorization();   // <-- token obligatoire pour cet endpoint
    }
}