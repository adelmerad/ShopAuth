using Microsoft.AspNetCore.Identity;

namespace ShopAuth.Endpoints;

// Catalogue des roles globaux (AspNetRoles). "admin" est protege : c'est le
// role qui donne acces a tout le panneau d'admin, le supprimer casserait
// l'application pour tout le monde.
public static class RoleEndpoints
{
    public record CreateRoleRequest(string Name);

    public static void MapRoleEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/admin/api/roles")
            .RequireAuthorization(policy => policy
                .AddAuthenticationSchemes(IdentityConstants.ApplicationScheme)
                .RequireRole("admin"))
            .AddEndpointFilter<RequireActiveAccountFilter>()
            .AddEndpointFilter<RequireAdminHeaderFilter>();

        group.MapGet("/", (RoleManager<IdentityRole> roleManager) =>
            Results.Ok(roleManager.Roles.Select(r => new { r.Id, r.Name, Protected = r.Name == "admin" })));

        group.MapPost("/", async (CreateRoleRequest request, RoleManager<IdentityRole> roleManager) =>
        {
            if (string.IsNullOrWhiteSpace(request.Name))
                return Results.BadRequest("Le nom du rôle est obligatoire.");

            // AspNetRoles.NormalizedName est un nvarchar(256) : sans ce controle,
            // un nom trop long fait echouer l'insertion en base et laisse fuiter
            // une exception EF Core/SQL Server brute au client.
            if (request.Name.Length > 256)
                return Results.BadRequest("Le nom du rôle ne peut pas dépasser 256 caractères.");

            if (await roleManager.RoleExistsAsync(request.Name))
                return Results.Conflict("Ce rôle existe déjà.");

            var result = await roleManager.CreateAsync(new IdentityRole(request.Name));
            return result.Succeeded ? Results.Ok() : Results.BadRequest(result.Errors.Select(e => e.Description));
        });

        group.MapDelete("/{id}", async (string id, RoleManager<IdentityRole> roleManager) =>
        {
            var role = await roleManager.FindByIdAsync(id);
            if (role is null)
                return Results.NotFound();

            if (role.Name == "admin")
                return Results.BadRequest("Le rôle admin est protégé, il ne peut pas être supprimé.");

            await roleManager.DeleteAsync(role);
            return Results.Ok();
        });
    }
}
