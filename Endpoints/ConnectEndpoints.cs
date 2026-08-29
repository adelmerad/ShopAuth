using System.Collections.Immutable;
using System.Security.Claims;
using ShopAuth.Data;
using ShopAuth.Entities;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace ShopAuth.Endpoints;

// Endpoint OAuth2 standard émis par OpenIddict : POST /connect/token
public static class ConnectEndpoints
{
    // Hash "bidon" pré-calculé une seule fois : sert à égaliser le temps de
    // réponse quand l'email est inconnu (anti-énumération par timing), sur
    // /connect/token ET /login.
    private static readonly string DummyPasswordHash =
        new PasswordHasher<ApplicationUser>()
            .HashPassword(new ApplicationUser(), "timing-attack-dummy-password");

    // Formulaire de connexion (page servie par GET /login).
    private const string LoginPageHtml = """
<!doctype html>
<html lang="fr">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>Connexion — ShopAuth</title>
  <style>
    body { font-family: system-ui, -apple-system, "Segoe UI", sans-serif; background:#f4f6f9; display:grid; place-items:center; min-height:100vh; margin:0; }
    .card { background:#fff; padding:32px; border-radius:14px; box-shadow:0 6px 24px rgba(20,25,40,.08); width:320px; }
    h1 { font-size:20px; margin:0 0 4px; }
    p.sub { color:#5a6472; margin:0 0 20px; font-size:14px; }
    label { display:block; font-size:13px; color:#333; margin-bottom:6px; }
    input { width:100%; padding:10px; margin-bottom:14px; border:1px solid #d5dbe6; border-radius:8px; box-sizing:border-box; font-size:14px; }
    button { width:100%; padding:11px; background:#2f56d9; color:#fff; border:0; border-radius:8px; font-size:15px; cursor:pointer; }
    button:hover { background:#2848b8; }
    .err { color:#c0293b; background:#fdeaec; padding:8px 10px; border-radius:8px; font-size:13px; margin:0 0 14px; }
  </style>
</head>
<body>
  <form class="card" method="post" action="/login">
    <h1>Connexion</h1>
    <p class="sub">Serveur d'authentification</p>
    __ERROR__
    <input type="hidden" name="returnUrl" value="__RETURN_URL__">
    <label for="email">Email</label>
    <input id="email" name="email" type="email" required autofocus>
    <label for="password">Mot de passe</label>
    <input id="password" name="password" type="password" required>
    <button type="submit">Se connecter</button>
  </form>
</body>
</html>
""";

    public static void MapConnectEndpoints(this WebApplication app)
    {
        app.MapPost("/connect/token", async (
            HttpContext httpContext,
            UserManager<ApplicationUser> userManager,
            SignInManager<ApplicationUser> signInManager,
            IOpenIddictScopeManager scopeManager,
            ApplicationDbContext db) =>
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
                {
                    // Email inconnu : vérification "à vide" pour égaliser le temps de
                    // réponse (anti-énumération par timing).
                    userManager.PasswordHasher.VerifyHashedPassword(
                        new ApplicationUser(), DummyPasswordHash, request.Password!);
                    return Forbid("Identifiants invalides.");
                }

                // Vérification AVEC lockout : notre anti-brute-force reste actif.
                var result = await signInManager.CheckPasswordSignInAsync(
                    user, request.Password!, lockoutOnFailure: true);
                if (result.IsLockedOut)
                    return Forbid(AccountStatusChecker.LockedOutMessage(user));
                if (!result.Succeeded)
                    return Forbid("Identifiants invalides.");

                if (await AccountStatusChecker.IsSuspendedAsync(db, user.Id))
                    return Forbid("Compte suspendu.", Errors.AccessDenied);

                if (!await HasAppAccessAsync(userManager, db, user, request.ClientId!))
                    return Forbid("Ce compte n'a aucun rôle pour cette application.", Errors.AccessDenied);

                var principal = await CreatePrincipalAsync(userManager, db, user, request.ClientId!, request.GetScopes());
                // Résout les audiences (aud) à partir des scopes demandés.
                principal.SetResources(await GetResourcesAsync(scopeManager, request.GetScopes()));
                return Results.SignIn(principal, null,
                    OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
            }

            // ----- Grant types : authorization_code + refresh_token -----
            // Dans les deux cas, l'identité est déjà encodée (dans le code ou dans
            // le refresh token) : OpenIddict la décode via son schéma serveur.
            if (request.IsAuthorizationCodeGrantType() || request.IsRefreshTokenGrantType())
            {
                var auth = await httpContext.AuthenticateAsync(
                    OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);

                var userId = auth.Principal?.GetClaim(Claims.Subject);
                var user = userId is null ? null : await userManager.FindByIdAsync(userId);
                if (user is null)
                    return Forbid("Le code ou le refresh token n'est plus valide.");

                // Revérifié à chaque refresh : une suspension ou un accès retiré
                // entre-temps coupe aussi le renouvellement, pas seulement les
                // nouvelles connexions - un refresh token deja valide ne doit pas
                // suffire a garder l'acces.
                if (await AccountStatusChecker.IsSuspendedAsync(db, user.Id))
                    return Forbid("Compte suspendu.", Errors.AccessDenied);

                if (!await HasAppAccessAsync(userManager, db, user, request.ClientId!))
                    return Forbid("Ce compte n'a aucun rôle pour cette application.", Errors.AccessDenied);

                var principal = await CreatePrincipalAsync(userManager, db, user, request.ClientId!, auth.Principal!.GetScopes());
                principal.SetResources(await GetResourcesAsync(scopeManager, auth.Principal!.GetScopes()));
                return Results.SignIn(principal, null,
                    OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
            }

            return Forbid("Type de grant non supporté.");
        });

        // ----- /connect/authorize : point d'entrée du login interactif -----
        app.MapGet("/connect/authorize", async (
            HttpContext httpContext,
            UserManager<ApplicationUser> userManager,
            IOpenIddictScopeManager scopeManager,
            ApplicationDbContext db) =>
        {
            var request = httpContext.GetOpenIddictServerRequest()
                ?? throw new InvalidOperationException("Requête OpenIddict introuvable.");

            // L'utilisateur a-t-il déjà une session (cookie) ?
            var result = await httpContext.AuthenticateAsync(IdentityConstants.ApplicationScheme);

            // Forcer la page de login si : pas de session, OU le client demande prompt=login.
            if (!result.Succeeded || request.HasPrompt(Prompts.Login))
            {
                // prompt=login : on ferme la session en cours pour forcer une vraie
                // ré-authentification, et on retire "prompt" de l'URL de retour (sinon boucle).
                if (result.Succeeded)
                    await httpContext.SignOutAsync(IdentityConstants.ApplicationScheme);

                var parameters = httpContext.Request.Query
                    .Where(p => p.Key != Parameters.Prompt)
                    .ToDictionary(p => p.Key, p => (string?)p.Value.ToString());
                var returnUrl = QueryHelpers.AddQueryString(
                    httpContext.Request.PathBase + httpContext.Request.Path, parameters);

                return Results.Redirect($"/login?returnUrl={Uri.EscapeDataString(returnUrl)}");
            }

            // Connecté -> on émet le code d'autorisation (OpenIddict redirige vers le client).
            var user = await userManager.GetUserAsync(result.Principal!)
                ?? throw new InvalidOperationException("Utilisateur introuvable.");

            // Un cookie deja valide ne doit pas suffire a obtenir de nouveaux jetons :
            // une suspension decidee entre-temps coupe aussi une session deja ouverte.
            if (await AccountStatusChecker.IsSuspendedAsync(db, user.Id))
            {
                await httpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
                return Forbid("Compte suspendu.", Errors.AccessDenied);
            }

            // Avoir un compte SSO valide ne suffit pas : il faut un rôle (global admin
            // ou applicatif) pour CETTE application precise, sinon access_denied.
            if (!await HasAppAccessAsync(userManager, db, user, request.ClientId!))
            {
                // On ferme la session pour permettre de reessayer avec un autre compte
                // sans avoir a vider les cookies a la main.
                await httpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
                return Forbid("Ce compte n'a aucun rôle pour cette application.", Errors.AccessDenied);
            }

            var principal = await CreatePrincipalAsync(userManager, db, user, request.ClientId!, request.GetScopes());
            principal.SetResources(await GetResourcesAsync(scopeManager, request.GetScopes()));
            return Results.SignIn(principal, null,
                OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        });

        // ----- Page de login (formulaire HTML minimal) -----
        app.MapGet("/login", (string? returnUrl, string? error, int? minutes) =>
        {
            var url = System.Net.WebUtility.HtmlEncode(returnUrl ?? "/");
            var errorHtml = error switch
            {
                "suspended" => "<p class=\"err\">Ce compte est suspendu.</p>",
                // Distinct d'un verrouillage temporaire : un meme message pour
                // les deux laisserait croire a quelqu'un qui s'est juste trompe
                // de mot de passe que son compte a ete coupe definitivement.
                "disabled" => "<p class=\"err\">Ce compte a été désactivé.</p>",
                "locked" => $"<p class=\"err\">Trop de tentatives échouées. Réessayez {(minutes is > 0 ? $"dans {minutes} minute{(minutes > 1 ? "s" : "")}" : "plus tard")}.</p>",
                "true" => "<p class=\"err\">Identifiants invalides.</p>",
                _ => ""
            };
            var html = LoginPageHtml
                .Replace("__RETURN_URL__", url)
                .Replace("__ERROR__", errorHtml);
            return Results.Content(html, "text/html");
        });

        app.MapPost("/login", async (
            HttpContext httpContext,
            UserManager<ApplicationUser> userManager,
            SignInManager<ApplicationUser> signInManager,
            ApplicationDbContext db) =>
        {
            var form = await httpContext.Request.ReadFormAsync();
            var email = form["email"].ToString();
            var password = form["password"].ToString();
            var returnUrl = form["returnUrl"].ToString();
            if (string.IsNullOrWhiteSpace(returnUrl)) returnUrl = "/";

            var user = await userManager.FindByEmailAsync(email);
            if (user is null)
            {
                // Email inconnu : même vérification "à vide" que sur /connect/token.
                userManager.PasswordHasher.VerifyHashedPassword(
                    new ApplicationUser(), DummyPasswordHash, password);
                return Results.Redirect($"/login?returnUrl={Uri.EscapeDataString(returnUrl)}&error=true");
            }

            // Pose le cookie de session ET applique le verrouillage (anti-brute-force).
            var loginResult = await signInManager.PasswordSignInAsync(
                user, password, isPersistent: false, lockoutOnFailure: true);
            if (loginResult.IsLockedOut)
            {
                if (user.LockoutEnd == DateTimeOffset.MaxValue)
                    return Results.Redirect($"/login?returnUrl={Uri.EscapeDataString(returnUrl)}&error=disabled");

                var mins = user.LockoutEnd is null
                    ? 1
                    : Math.Max(1, (int)Math.Ceiling((user.LockoutEnd.Value - DateTimeOffset.UtcNow).TotalMinutes));
                return Results.Redirect($"/login?returnUrl={Uri.EscapeDataString(returnUrl)}&error=locked&minutes={mins}");
            }
            if (!loginResult.Succeeded)
                return Results.Redirect($"/login?returnUrl={Uri.EscapeDataString(returnUrl)}&error=true");

            if (await AccountStatusChecker.IsSuspendedAsync(db, user.Id))
            {
                await httpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
                return Results.Redirect($"/login?returnUrl={Uri.EscapeDataString(returnUrl)}&error=suspended");
            }

            return Results.Redirect(returnUrl);
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

    // Un compte a-t-il le droit d'utiliser cette application (client_id) ?
    // Vrai si : role global "admin" (passe-partout), OU au moins un role
    // applicatif enregistre pour ce client_id precis dans UserApplicationRoles.
    private static async Task<bool> HasAppAccessAsync(
        UserManager<ApplicationUser> userManager, ApplicationDbContext db, ApplicationUser user, string clientId)
    {
        var globalRoles = await userManager.GetRolesAsync(user);
        if (globalRoles.Contains("admin"))
            return true;

        return await db.UserApplicationRoles
            .AnyAsync(x => x.UserId == user.Id && x.ClientId == clientId);
    }

    // Construit l'identité (claims + scopes + destinations) à partir de l'utilisateur.
    // Fusionne les roles globaux (AspNetUserRoles) et les roles specifiques a
    // cette application (UserApplicationRoles) - union, jamais remplacement :
    // un admin reste admin partout.
    private static async Task<ClaimsPrincipal> CreatePrincipalAsync(
        UserManager<ApplicationUser> userManager, ApplicationDbContext db, ApplicationUser user,
        string clientId, IEnumerable<string> scopes)
    {
        var identity = new ClaimsIdentity(
            authenticationType: TokenValidationParameters.DefaultAuthenticationType,
            nameType: Claims.Name,
            roleType: Claims.Role);

        identity.SetClaim(Claims.Subject, user.Id)
                .SetClaim(Claims.Email, user.Email)
                .SetClaim(Claims.Name, user.UserName);

        var globalRoles = await userManager.GetRolesAsync(user);
        var appRoles = await db.UserApplicationRoles
            .Where(x => x.UserId == user.Id && x.ClientId == clientId)
            .Select(x => x.Role.Name!)
            .ToListAsync();

        foreach (var role in globalRoles.Concat(appRoles).Distinct())
            identity.AddClaim(Claims.Role, role);

        identity.SetScopes(scopes);
        identity.SetDestinations(GetDestinations);

        return new ClaimsPrincipal(identity);
    }

    // Réponse d'échec au format OAuth2 (invalid_grant par défaut, ou un autre
    // code d'erreur standard comme access_denied).
    private static IResult Forbid(string description, string error = Errors.InvalidGrant) =>
        Results.Forbid(
            authenticationSchemes: new[] { OpenIddictServerAspNetCoreDefaults.AuthenticationScheme },
            properties: new AuthenticationProperties(new Dictionary<string, string?>
            {
                [OpenIddictServerAspNetCoreConstants.Properties.Error] = error,
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

            // Le role doit être visible côté client (ShopWebApp) pour l'autorisation,
            // donc aussi dans l'id_token, pas seulement l'access_token.
            case Claims.Role:
                yield return Destinations.AccessToken;
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
