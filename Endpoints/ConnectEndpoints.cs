using System.Collections.Immutable;
using System.Security.Claims;
using AuthApiTest.Entities;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace AuthApiTest.Endpoints;

// Endpoint OAuth2 standard émis par OpenIddict : POST /connect/token
public static class ConnectEndpoints
{
    public static void MapConnectEndpoints(this WebApplication app)
    {
        app.MapPost("/connect/token", async (
            HttpContext httpContext,
            UserManager<ApplicationUser> userManager,
            SignInManager<ApplicationUser> signInManager,
            IOpenIddictScopeManager scopeManager) =>
        {
            // OpenIddict a déjà validé la requête (client_id, grant_type...) ;
            // on récupère ses paramètres normalisés.
            var request = httpContext.GetOpenIddictServerRequest()
                ?? throw new InvalidOperationException("Requête OpenIddict introuvable.");

            // ----- Grant type : password (username + password) -----
            if (request.IsPasswordGrantType())
            {
                var user = await userManager.FindByNameAsync(request.Username!);
                if (user is null)
                    return Forbid("Identifiants invalides.");

                // Vérification AVEC lockout : notre anti-brute-force reste actif.
                var result = await signInManager.CheckPasswordSignInAsync(
                    user, request.Password!, lockoutOnFailure: true);
                if (!result.Succeeded)
                    return Forbid("Identifiants invalides ou compte verrouillé.");

                var principal = CreatePrincipal(user, request.GetScopes());
                // Résout les audiences (aud) à partir des scopes demandés.
                principal.SetResources(await GetResourcesAsync(scopeManager, request.GetScopes()));
                return Results.SignIn(principal, null,
                    OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
            }

            // ----- Grant type : refresh_token -----
            if (request.IsRefreshTokenGrantType())
            {
                // Le principal est encodé dans le refresh token : OpenIddict le décode
                // pour nous quand on authentifie sur son schéma serveur.
                var auth = await httpContext.AuthenticateAsync(
                    OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);

                var userId = auth.Principal?.GetClaim(Claims.Subject);
                var user = userId is null ? null : await userManager.FindByIdAsync(userId);
                if (user is null)
                    return Forbid("Le refresh token n'est plus valide.");

                var principal = CreatePrincipal(user, auth.Principal!.GetScopes());
                principal.SetResources(await GetResourcesAsync(scopeManager, auth.Principal!.GetScopes()));
                return Results.SignIn(principal, null,
                    OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
            }

            return Forbid("Type de grant non supporté.");
        });
    }

    // Résout les ressources (audiences) associées aux scopes demandés.
    private static async Task<List<string>> GetResourcesAsync(
        IOpenIddictScopeManager scopeManager, IEnumerable<string> scopes)
    {
        var resources = new List<string>();
        await foreach (var resource in scopeManager.ListResourcesAsync(scopes.ToImmutableArray()))
            resources.Add(resource);
        return resources;
    }

    // Construit l'identité (claims + scopes + destinations) à partir de l'utilisateur.
    private static ClaimsPrincipal CreatePrincipal(ApplicationUser user, IEnumerable<string> scopes)
    {
        var identity = new ClaimsIdentity(
            authenticationType: TokenValidationParameters.DefaultAuthenticationType,
            nameType: Claims.Name,
            roleType: Claims.Role);

        identity.SetClaim(Claims.Subject, user.Id)
                .SetClaim(Claims.Email, user.Email)
                .SetClaim(Claims.Name, user.UserName);

        identity.SetScopes(scopes);
        identity.SetDestinations(GetDestinations);

        return new ClaimsPrincipal(identity);
    }

    // Réponse d'échec au format OAuth2 (invalid_grant + description).
    private static IResult Forbid(string description) =>
        Results.Forbid(
            authenticationSchemes: new[] { OpenIddictServerAspNetCoreDefaults.AuthenticationScheme },
            properties: new AuthenticationProperties(new Dictionary<string, string?>
            {
                [OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.InvalidGrant,
                [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = description
            }));

    // Indique, pour chaque claim, dans quel(s) token(s) il doit apparaître.
    private static IEnumerable<string> GetDestinations(Claim claim)
    {
        switch (claim.Type)
        {
            case Claims.Name:
                yield return Destinations.AccessToken;
                if (claim.Subject!.HasScope(Scopes.Profile))
                    yield return Destinations.IdentityToken;
                yield break;

            case Claims.Email:
                yield return Destinations.AccessToken;
                if (claim.Subject!.HasScope(Scopes.Email))
                    yield return Destinations.IdentityToken;
                yield break;

            // Ne jamais exposer le security stamp d'Identity dans un token.
            case "AspNet.Identity.SecurityStamp":
                yield break;

            default:
                yield return Destinations.AccessToken;
                yield break;
        }
    }
}
