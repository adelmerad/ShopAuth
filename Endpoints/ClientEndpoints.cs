using Microsoft.AspNetCore.Identity;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace ShopAuth.Endpoints;

// Gestion des applications OAuth (clients OpenIddict) depuis le panneau
// d'admin - stockees en base par OpenIddict, donc modifiables a chaud sans
// redemarrer le serveur (contrairement aux clients seedes en dur au demarrage).
public static class ClientEndpoints
{
    public record CreateClientRequest(string ClientId, string DisplayName, string[] RedirectUris);
    public record UpdateClientRequest(string DisplayName, string[] RedirectUris);

    public static void MapClientEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/admin/api/clients")
            .RequireAuthorization(policy => policy
                .AddAuthenticationSchemes(IdentityConstants.ApplicationScheme)
                .RequireRole("admin"))
            .AddEndpointFilter<RequireActiveAccountFilter>()
            .AddEndpointFilter<RequireAdminHeaderFilter>();

        group.MapGet("/", async (IOpenIddictApplicationManager manager) =>
        {
            var result = new List<object>();
            await foreach (var app in manager.ListAsync())
            {
                result.Add(new
                {
                    clientId = await manager.GetClientIdAsync(app),
                    displayName = await manager.GetDisplayNameAsync(app),
                    redirectUris = await manager.GetRedirectUrisAsync(app)
                });
            }
            return Results.Ok(result);
        });

        group.MapPost("/", async (CreateClientRequest request, IOpenIddictApplicationManager manager) =>
        {
            if (await manager.FindByClientIdAsync(request.ClientId) is not null)
                return Results.Conflict("Ce client_id existe déjà.");

            var secret = GenerateSecret();
            var descriptor = new OpenIddictApplicationDescriptor
            {
                ClientId = request.ClientId,
                ClientType = ClientTypes.Confidential,
                ClientSecret = secret,
                ConsentType = ConsentTypes.Implicit,
                DisplayName = request.DisplayName,
                Permissions =
                {
                    Permissions.Endpoints.Token,
                    Permissions.Endpoints.Authorization,
                    Permissions.GrantTypes.AuthorizationCode,
                    Permissions.GrantTypes.RefreshToken,
                    Permissions.ResponseTypes.Code,
                    Permissions.Scopes.Email,
                    Permissions.Scopes.Profile
                },
                Requirements = { Requirements.Features.ProofKeyForCodeExchange }
            };
            foreach (var uri in request.RedirectUris)
                descriptor.RedirectUris.Add(new Uri(uri));

            await manager.CreateAsync(descriptor);

            // Le secret n'est renvoye qu'a la creation : impossible de le relire
            // ensuite (OpenIddict ne stocke que son hash).
            return Results.Created($"/admin/api/clients/{request.ClientId}", new { request.ClientId, secret });
        });

        group.MapPut("/{clientId}", async (string clientId, UpdateClientRequest request, IOpenIddictApplicationManager manager) =>
        {
            var app = await manager.FindByClientIdAsync(clientId);
            if (app is null)
                return Results.NotFound();

            var descriptor = new OpenIddictApplicationDescriptor();
            await manager.PopulateAsync(descriptor, app);
            descriptor.DisplayName = request.DisplayName;
            descriptor.RedirectUris.Clear();
            foreach (var uri in request.RedirectUris)
                descriptor.RedirectUris.Add(new Uri(uri));

            await manager.UpdateAsync(app, descriptor);
            return Results.Ok();
        });

        group.MapPost("/{clientId}/rotate-secret", async (string clientId, IOpenIddictApplicationManager manager) =>
        {
            var app = await manager.FindByClientIdAsync(clientId);
            if (app is null)
                return Results.NotFound();

            var secret = GenerateSecret();
            var descriptor = new OpenIddictApplicationDescriptor();
            await manager.PopulateAsync(descriptor, app);
            descriptor.ClientSecret = secret;

            await manager.UpdateAsync(app, descriptor);
            return Results.Ok(new { secret });
        });

        group.MapDelete("/{clientId}", async (string clientId, IOpenIddictApplicationManager manager) =>
        {
            var app = await manager.FindByClientIdAsync(clientId);
            if (app is null)
                return Results.NotFound();

            await manager.DeleteAsync(app);
            return Results.Ok();
        });
    }

    private static string GenerateSecret() =>
        Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
}
