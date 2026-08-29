using ShopAuth.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace ShopAuth.Data;

public static class DbSeeder
{
    public static async Task SeedAsync(IServiceProvider services)
    {
        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
        var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();

        const string email = "test@entreprise.com";

        // Le rôle "admin" doit exister avant qu'on essaie de l'assigner.
        if (!await roleManager.RoleExistsAsync("admin"))
            await roleManager.CreateAsync(new IdentityRole("admin"));

        var user = await userManager.FindByEmailAsync(email);
        if (user is null)
        {
            user = new ApplicationUser
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

        // En dehors du if : s'applique aussi bien à un utilisateur tout juste
        // créé qu'à un utilisateur déjà existant qui n'aurait pas encore le rôle.
        if (!await userManager.IsInRoleAsync(user, "admin"))
            await userManager.AddToRoleAsync(user, "admin");

        // Deuxieme compte de test, SANS role global : sert a demontrer/tester les
        // roles par application (UserApplicationRoles) - ce compte n'a acces a
        // aucune application tant qu'un role applicatif ne lui est pas donne.
        if (!await roleManager.RoleExistsAsync("employe"))
            await roleManager.CreateAsync(new IdentityRole("employe"));

        const string employeEmail = "employe@entreprise.com";
        var employe = await userManager.FindByEmailAsync(employeEmail);
        if (employe is null)
        {
            employe = new ApplicationUser { UserName = employeEmail, Email = employeEmail };
            await userManager.CreateAsync(employe, "MotDePasse123!");
        }

        // Demo du role applicatif : "employe" n'a AUCUN role global, mais peut
        // quand meme utiliser shopwebapp-bff grace a ce role scope a ce client_id.
        var db = services.GetRequiredService<ApplicationDbContext>();
        var employeRole = await roleManager.FindByNameAsync("employe");
        var hasAppRole = await db.UserApplicationRoles.AnyAsync(x =>
            x.UserId == employe.Id && x.ClientId == "shopwebapp-bff" && x.RoleId == employeRole!.Id);
        if (!hasAppRole)
        {
            db.UserApplicationRoles.Add(new UserApplicationRole
            {
                UserId = employe.Id,
                ClientId = "shopwebapp-bff",
                RoleId = employeRole!.Id
            });
            await db.SaveChangesAsync();
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
                new Uri("http://172.20.10.4:5200/signin-oidc")
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
                Permissions.Scopes.Profile
            }
        };

        var existing = await manager.FindByClientIdAsync(clientId);
        if (existing is null)
            await manager.CreateAsync(descriptor);
        else
            await manager.UpdateAsync(existing, descriptor);
    }

    // Enregistre le client CONFIDENTIEL dédié à ShopWebApp (idempotent, upsert).
    // Séparé de "postman" : "postman" reste public car Swagger tourne dans le
    // navigateur et ne peut jamais garder un secret en sécurité. ShopWebApp,
    // lui, tourne côté serveur : il peut (et doit) garder un vrai secret.
    public static async Task SeedShopWebAppClientAsync(IServiceProvider services)
    {
        var manager = services.GetRequiredService<IOpenIddictApplicationManager>();

        const string clientId = "shopwebapp-bff";

        var descriptor = new OpenIddictApplicationDescriptor
        {
            ClientId = clientId,
            ClientType = ClientTypes.Confidential,
            ClientSecret = "shopwebapp-secret-dev-only", // dev uniquement, projet d'apprentissage
            ConsentType = ConsentTypes.Implicit,
            DisplayName = "ShopWebApp (BFF)",

            // /auth/callback : ShopWebApp gère l'échange lui-même (PKCE manuel),
            // plus le middleware AddOpenIdConnect et son /signin-oidc par défaut.
            RedirectUris =
            {
                new Uri("http://localhost:5200/auth/callback"),
                new Uri("http://192.168.100.9:5200/auth/callback"),
                new Uri("http://172.20.10.4:5200/auth/callback")
            },

            Permissions =
            {
                Permissions.Endpoints.Token,
                Permissions.Endpoints.Authorization,

                Permissions.GrantTypes.AuthorizationCode,
                Permissions.GrantTypes.RefreshToken,

                Permissions.ResponseTypes.Code,

                Permissions.Scopes.Email,
                Permissions.Scopes.Profile,
                Permissions.Prefixes.Scope + Scopes.OfflineAccess // necessaire pour obtenir un refresh_token
            },

            Requirements =
            {
                Requirements.Features.ProofKeyForCodeExchange
            }
        };

        var existing = await manager.FindByClientIdAsync(clientId);
        if (existing is null)
            await manager.CreateAsync(descriptor);
        else
            await manager.UpdateAsync(existing, descriptor);
    }
}