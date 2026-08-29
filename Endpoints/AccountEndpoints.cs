using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using ShopAuth.Data;
using ShopAuth.Entities;

namespace ShopAuth.Endpoints;

// Connexion directe par cookie, separee du flow OAuth (/connect/...).
// Sert au panneau d'admin : pas besoin de passer par tout le protocole
// OAuth2 juste pour se connecter a une page d'administration interne.
public static class AccountEndpoints
{
    public record LoginRequest(string Email, string Password);

    public static void MapAccountEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/account");

        group.MapPost("/login", async (
            LoginRequest request,
            UserManager<ApplicationUser> userManager,
            SignInManager<ApplicationUser> signInManager,
            ApplicationDbContext db) =>
        {
            var user = await userManager.FindByEmailAsync(request.Email);
            if (user is null)
                return Results.Unauthorized();

            var result = await signInManager.PasswordSignInAsync(
                user, request.Password, isPersistent: false, lockoutOnFailure: true);

            if (result.IsLockedOut)
                return Results.Json(new { error = AccountStatusChecker.LockedOutMessage(user) }, statusCode: StatusCodes.Status423Locked);

            if (!result.Succeeded)
                return Results.Unauthorized();

            if (await AccountStatusChecker.IsSuspendedAsync(db, user.Id))
            {
                await signInManager.SignOutAsync();
                return Results.Json(new { error = "Ce compte est suspendu." }, statusCode: StatusCodes.Status403Forbidden);
            }

            return Results.Ok();
        });

        group.MapPost("/logout", async (SignInManager<ApplicationUser> signInManager) =>
        {
            await signInManager.SignOutAsync();
            return Results.Ok();
        });

        group.MapGet("/session", async (
            HttpContext context,
            UserManager<ApplicationUser> userManager) =>
        {
            var user = await userManager.GetUserAsync(context.User)
                ?? throw new InvalidOperationException("Utilisateur introuvable.");

            var roles = await userManager.GetRolesAsync(user);
            return Results.Ok(new { id = user.Id, email = user.Email, roles });
        }).RequireAuthorization(policy => policy
            .AddAuthenticationSchemes(IdentityConstants.ApplicationScheme)
            .RequireAuthenticatedUser());
    }
}
