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

    // Enregistre l'application cliente OpenIddict (idempotent).
    public static async Task SeedOpenIddictClientAsync(IServiceProvider services)
    {
        var manager = services.GetRequiredService<IOpenIddictApplicationManager>();

        const string clientId = "postman";

        // Déjà présent ? On ne fait rien.
        if (await manager.FindByClientIdAsync(clientId) is not null)
            return;

        await manager.CreateAsync(new OpenIddictApplicationDescriptor
        {
            ClientId = clientId,
            ClientType = ClientTypes.Public,          // client public : pas de secret
            ConsentType = ConsentTypes.Implicit,      // auto-approuve (pas d'écran de consentement)
            DisplayName = "Client de test (Postman / Swagger)",

            // Où OpenIddict a le droit de renvoyer après login (Swagger + notre BFF WebApp).
            RedirectUris =
            {
                new Uri("http://localhost:5124/swagger/oauth2-redirect.html"),
                new Uri("http://localhost:5200/signin-oidc")
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
        });
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