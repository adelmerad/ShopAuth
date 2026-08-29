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
            .AddEndpointFilter<RequireAdminHeaderFilter>();

        group.MapGet("/", (RoleManager<IdentityRole> roleManager) =>
            Results.Ok(roleManager.Roles.Select(r => new { r.Id, r.Name, Protected = r.Name == "admin" })));

        group.MapPost("/", async (CreateRoleRequest request, RoleManager<IdentityRole> roleManager) =>
        {
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
