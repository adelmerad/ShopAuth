using ShopAuth.Entities;
using Microsoft.AspNetCore.Identity;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace ShopAuth.Data;

public static class DbSeeder
{
    public static async Task SeedAsync(IServiceProvider services)
    {
        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();

        const string email = "test@entreprise.com";

        // Déjà présent ? On ne fait rien (idempotent)
        if (await userManager.FindByEmailAsync(email) is not null)
            return;

        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            MustChangePassword = true
        };

        var result = await userManager.CreateAsync(user, "MotDePasseInitial123!");

        if (!result.Succeeded)
        {
            var errors = string.Join(", ", result.Errors.Select(e => e.Description));
            throw new Exception($"Échec du seed : {errors}");
        }
    }

    // Enregistre l'application cliente OpenIddict. Idempotent au sens fort :
    // si le client existe déjà, on met à jour ses infos (ex: nouvelle IP LAN
    // dans RedirectUris) au lieu de les ignorer.
    public static async Task SeedOpenIddictClientAsync(IServiceProvider services)
    {
        var manager = services.GetRequiredService<IOpenIddictApplicationManager>();

        const string clientId = "postman";

        var descriptor = new OpenIddictApplicationDescriptor
        {
            ClientId = clientId,
            ClientType = ClientTypes.Public,          // client public : pas de secret
            ConsentType = ConsentTypes.Implicit,      // auto-approuve (pas d'écran de consentement)
            DisplayName = "Client de test (Postman / Swagger)",

            // Où OpenIddict a le droit de renvoyer après login (Swagger + notre BFF
            // ShopWebApp, en local ET depuis le réseau local pour tester depuis un autre PC).
            RedirectUris =
            {
                new Uri("http://localhost:5124/swagger/oauth2-redirect.html"),
                new Uri("http://localhost:5200/signin-oidc"),
                new Uri("http://192.168.100.9:5200/signin-oidc")
            },

            Permissions =
            {
                // Endpoints autorisés
                Permissions.Endpoints.Token,
                Permissions.Endpoints.Authorization,

                // Flows autorisés
                Permissions.GrantTypes.Password,
                Permissions.GrantTypes.AuthorizationCode,
                Permissions.GrantTypes.RefreshToken,

                // Type de réponse (code) pour le flow authorization code
                Permissions.ResponseTypes.Code,

                // Scopes que ce client peut demander
                Permissions.Scopes.Email,
                Permissions.Scopes.Profile,
                Permissions.Prefixes.Scope + "shop_api"
            }
        };

        var existing = await manager.FindByClientIdAsync(clientId);
        if (existing is null)
            await manager.CreateAsync(descriptor);
        else
            await manager.UpdateAsync(existing, descriptor);
    }

    // Enregistre le scope d'API "shop_api" (idempotent).
    // Sa "resource" devient l'audience (aud) portée par les access tokens,
    // que ShopApi vérifiera pour n'accepter que les tokens qui lui sont destinés.
    public static async Task SeedApiScopeAsync(IServiceProvider services)
    {
        var scopeManager = services.GetRequiredService<IOpenIddictScopeManager>();

        const string scopeName = "shop_api";

        if (await scopeManager.FindByNameAsync(scopeName) is not null)
            return;

        await scopeManager.CreateAsync(new OpenIddictScopeDescriptor
        {
            Name = scopeName,
            DisplayName = "Accès à ShopApi",
            Resources = { "shop_api" }   // -> devient l'aud du token
        });
    }
}