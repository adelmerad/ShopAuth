using AuthApiTest.Entities;
using Microsoft.AspNetCore.Identity;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace AuthApiTest.Data;

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
            DisplayName = "Client de test (Postman / Swagger)",
            Permissions =
            {
                // Endpoint autorisé
                Permissions.Endpoints.Token,

                // Flows autorisés
                Permissions.GrantTypes.Password,
                Permissions.GrantTypes.RefreshToken,

                // Scopes que ce client peut demander
                Permissions.Scopes.Email,
                Permissions.Scopes.Profile
            }
        });
    }
}