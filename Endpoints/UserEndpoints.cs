using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using ShopAuth.Data;
using ShopAuth.Entities;

namespace ShopAuth.Endpoints;

// Gestion des comptes depuis le panneau d'admin. Toutes les routes exigent
// le role global "admin", verifie sur le cookie Identity (le meme que celui
// pose par /api/account/login), pas sur un token OAuth.
public static class UserEndpoints
{
    public record CreateUserRequest(string Email, string Password);
    public record SetRolesRequest(string[] Roles);
    public record AddAppRoleRequest(string ClientId, string RoleName);
    public record ResetPasswordRequest(string NewPassword);
    public record CreateSuspensionRequest(DateTimeOffset StartsAt, DateTimeOffset EndsAt, SuspensionReason Reason, string? Note);

    public static void MapUserEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/admin/api/users")
            .RequireAuthorization(policy => policy
                .AddAuthenticationSchemes(IdentityConstants.ApplicationScheme)
                .RequireRole("admin"))
            .AddEndpointFilter<RequireAdminHeaderFilter>();

        group.MapGet("/", async (UserManager<ApplicationUser> userManager, ApplicationDbContext db) =>
        {
            var users = await userManager.Users.ToListAsync();
            var now = DateTimeOffset.UtcNow;

            var result = new List<object>();
            foreach (var user in users)
            {
                var globalRoles = await userManager.GetRolesAsync(user);
                var appRoles = await db.UserApplicationRoles
                    .Where(x => x.UserId == user.Id)
                    .Select(x => new { x.Id, x.ClientId, RoleName = x.Role.Name })
                    .ToListAsync();
                var activeSuspension = await db.UserSuspensions
                    .Where(s => s.UserId == user.Id && s.StartsAt <= now && now <= s.EndsAt)
                    .Select(s => new { s.Id, s.StartsAt, s.EndsAt, s.Reason, s.Note })
                    .FirstOrDefaultAsync();

                result.Add(new
                {
                    user.Id,
                    user.Email,
                    isLockedOut = user.LockoutEnd is not null && user.LockoutEnd > now,
                    globalRoles,
                    appRoles,
                    activeSuspension
                });
            }

            return Results.Ok(result);
        });

        group.MapPost("/", async (CreateUserRequest request, UserManager<ApplicationUser> userManager) =>
        {
            var user = new ApplicationUser { UserName = request.Email, Email = request.Email };
            var result = await userManager.CreateAsync(user, request.Password);

            return result.Succeeded
                ? Results.Created($"/admin/api/users/{user.Id}", new { user.Id, user.Email })
                : Results.BadRequest(result.Errors.Select(e => e.Description));
        });

        group.MapPut("/{id}/roles", async (
            string id,
            SetRolesRequest request,
            HttpContext context,
            UserManager<ApplicationUser> userManager) =>
        {
            var user = await userManager.FindByIdAsync(id);
            if (user is null)
                return Results.NotFound();

            var currentUserId = userManager.GetUserId(context.User);
            var wasAdmin = await userManager.IsInRoleAsync(user, "admin");
            var willBeAdmin = request.Roles.Contains("admin");

            if (id == currentUserId && wasAdmin && !willBeAdmin)
                return Results.BadRequest("Impossible de retirer son propre rôle admin.");

            if (wasAdmin && !willBeAdmin && await CountActiveAdminsAsync(userManager) <= 1)
                return Results.BadRequest("Il doit rester au moins un administrateur actif.");

            var current = await userManager.GetRolesAsync(user);
            await userManager.RemoveFromRolesAsync(user, current);
            await userManager.AddToRolesAsync(user, request.Roles);

            return Results.Ok();
        });

        group.MapPost("/{id}/app-roles", async (string id, AddAppRoleRequest request, ApplicationDbContext db, RoleManager<IdentityRole> roleManager) =>
        {
            var role = await roleManager.FindByNameAsync(request.RoleName);
            if (role is null)
                return Results.BadRequest("Rôle inconnu.");

            var exists = await db.UserApplicationRoles.AnyAsync(x =>
                x.UserId == id && x.ClientId == request.ClientId && x.RoleId == role.Id);
            if (exists)
                return Results.Conflict("Ce rôle applicatif existe déjà pour cet utilisateur.");

            db.UserApplicationRoles.Add(new UserApplicationRole
            {
                UserId = id,
                ClientId = request.ClientId,
                RoleId = role.Id
            });
            await db.SaveChangesAsync();

            return Results.Ok();
        });

        group.MapDelete("/{id}/app-roles/{appRoleId:int}", async (string id, int appRoleId, ApplicationDbContext db) =>
        {
            var appRole = await db.UserApplicationRoles
                .FirstOrDefaultAsync(x => x.Id == appRoleId && x.UserId == id);
            if (appRole is null)
                return Results.NotFound();

            db.UserApplicationRoles.Remove(appRole);
            await db.SaveChangesAsync();

            return Results.Ok();
        });

        group.MapPost("/{id}/reset-password", async (string id, ResetPasswordRequest request, UserManager<ApplicationUser> userManager) =>
        {
            var user = await userManager.FindByIdAsync(id);
            if (user is null)
                return Results.NotFound();

            var token = await userManager.GeneratePasswordResetTokenAsync(user);
            var result = await userManager.ResetPasswordAsync(user, token, request.NewPassword);

            return result.Succeeded ? Results.Ok() : Results.BadRequest(result.Errors.Select(e => e.Description));
        });

        group.MapPost("/{id}/disable", async (
            string id,
            HttpContext context,
            UserManager<ApplicationUser> userManager) =>
        {
            var user = await userManager.FindByIdAsync(id);
            if (user is null)
                return Results.NotFound();

            var currentUserId = userManager.GetUserId(context.User);
            if (id == currentUserId)
                return Results.BadRequest("Impossible de désactiver son propre compte.");

            if (await userManager.IsInRoleAsync(user, "admin") && await CountActiveAdminsAsync(userManager) <= 1)
                return Results.BadRequest("Il doit rester au moins un administrateur actif.");

            // Le verrouillage seul ne coupe pas les sessions deja ouvertes : le
            // SecurityStamp force la revalidation du cookie cote client.
            await userManager.SetLockoutEndDateAsync(user, DateTimeOffset.MaxValue);
            await userManager.UpdateSecurityStampAsync(user);

            return Results.Ok();
        });

        group.MapPost("/{id}/enable", async (string id, UserManager<ApplicationUser> userManager) =>
        {
            var user = await userManager.FindByIdAsync(id);
            if (user is null)
                return Results.NotFound();

            await userManager.SetLockoutEndDateAsync(user, null);
            return Results.Ok();
        });

        group.MapPost("/{id}/suspensions", async (string id, CreateSuspensionRequest request, ApplicationDbContext db, UserManager<ApplicationUser> userManager) =>
        {
            if (await userManager.FindByIdAsync(id) is null)
                return Results.NotFound();

            if (request.EndsAt <= request.StartsAt)
                return Results.BadRequest("La date de fin doit être après la date de début.");

            // Deux periodes [A,B) et [C,D) se chevauchent si A < D ET C < B.
            var overlaps = await db.UserSuspensions.AnyAsync(s =>
                s.UserId == id && request.StartsAt < s.EndsAt && s.StartsAt < request.EndsAt);
            if (overlaps)
                return Results.BadRequest("Cette période chevauche une suspension déjà existante pour ce compte.");

            db.UserSuspensions.Add(new UserSuspension
            {
                UserId = id,
                StartsAt = request.StartsAt,
                EndsAt = request.EndsAt,
                Reason = request.Reason,
                Note = request.Note
            });
            await db.SaveChangesAsync();

            return Results.Ok();
        });

        group.MapDelete("/{id}/suspensions/{suspensionId:int}", async (string id, int suspensionId, ApplicationDbContext db) =>
        {
            var suspension = await db.UserSuspensions
                .FirstOrDefaultAsync(s => s.Id == suspensionId && s.UserId == id);
            if (suspension is null)
                return Results.NotFound();

            db.UserSuspensions.Remove(suspension);
            await db.SaveChangesAsync();

            return Results.Ok();
        });

        group.MapDelete("/{id}", async (
            string id,
            HttpContext context,
            UserManager<ApplicationUser> userManager) =>
        {
            var user = await userManager.FindByIdAsync(id);
            if (user is null)
                return Results.NotFound();

            var currentUserId = userManager.GetUserId(context.User);
            if (id == currentUserId)
                return Results.BadRequest("Impossible de supprimer son propre compte.");

            if (await userManager.IsInRoleAsync(user, "admin") && await CountActiveAdminsAsync(userManager) <= 1)
                return Results.BadRequest("Il doit rester au moins un administrateur actif.");

            await userManager.DeleteAsync(user);
            return Results.Ok();
        });
    }

    // Un admin "actif" = role admin + pas (encore) verrouille. Un admin suspendu
    // ou desactive ne compte pas comme un rempart valable contre le zero-admin.
    private static async Task<int> CountActiveAdminsAsync(UserManager<ApplicationUser> userManager)
    {
        var admins = await userManager.GetUsersInRoleAsync("admin");
        var now = DateTimeOffset.UtcNow;
        return admins.Count(a => a.LockoutEnd is null || a.LockoutEnd <= now);
    }
}
