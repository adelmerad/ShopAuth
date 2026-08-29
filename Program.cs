using ShopAuth.Endpoints;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Validation.AspNetCore;
using Microsoft.OpenApi.Models;
using ShopAuth.Data;
using ShopAuth.Entities;
using static OpenIddict.Abstractions.OpenIddictConstants;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<ApplicationDbContext>(options =>
{
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection"));

    // Enregistre les entités OpenIddict (Applications, Scopes, Authorizations,
    // Tokens) dans ce DbContext : EF Core créera et gérera leurs tables.
    options.UseOpenIddict();
});

builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
{
    options.Password.RequiredLength = 8;
    options.User.RequireUniqueEmail = true;

    // Verrouillage anti-brute-force : après 5 échecs, le compte est bloqué 15 min.
    // Les colonnes AccessFailedCount / LockoutEnd existent déjà (schéma Identity).
    options.Lockout.MaxFailedAccessAttempts = 5;
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    options.Lockout.AllowedForNewUsers = true;
})

.AddEntityFrameworkStores<ApplicationDbContext>()
.AddDefaultTokenProviders();

// Cookie de session pour le login interactif (flow authorization code) :
// quand /connect/authorize a besoin d'un utilisateur connecté, il redirige ici.
// Meme cookie utilise par l'API d'admin (/admin/api, /api/account) : la, une
// redirection HTML n'a pas de sens pour un appel fetch() depuis le panneau
// React - on renvoie un vrai code de statut a la place.
builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/login";
    options.ExpireTimeSpan = TimeSpan.FromMinutes(10); // session d'auth courte (10 min)
    options.SlidingExpiration = false;                 // expiration FERME

    options.Events.OnRedirectToLogin = context =>
    {
        if (IsApiRequest(context.Request))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        }
        context.Response.Redirect(context.RedirectUri);
        return Task.CompletedTask;
    };

    options.Events.OnRedirectToAccessDenied = context =>
    {
        if (IsApiRequest(context.Request))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        }
        context.Response.Redirect(context.RedirectUri);
        return Task.CompletedTask;
    };
});

static bool IsApiRequest(HttpRequest request) =>
    request.Path.StartsWithSegments("/admin/api") || request.Path.StartsWithSegments("/api/account");

// --- Authentification : on valide désormais les tokens émis par OpenIddict ---
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme;
});

builder.Services.AddOpenIddict()

    // 1) CORE : où OpenIddict stocke ses données (nos tables EF Core)
    .AddCore(options =>
    {
        options.UseEntityFrameworkCore()
               .UseDbContext<ApplicationDbContext>();
    })

    // 2) SERVER : émet les tokens
    .AddServer(options =>
    {
        // Endpoints OAuth2 : token + autorisation (login interactif)
        options.SetTokenEndpointUris("connect/token");
        options.SetAuthorizationEndpointUris("connect/authorize");

        // Issuer FIXE (pas déduit dynamiquement de la requête entrante). Nécessaire
        // dès qu'on peut atteindre ce serveur par plusieurs adresses (localhost ET
        // IP LAN) : sans ça, un code émis via une requête arrivée par l'IP LAN et
        // échangé via un appel serveur-à-serveur en localhost seraient vus comme
        // deux issuers différents -> "invalid_grant : issuer not valid". Doit
        // rester identique à SetIssuer(...) côté validation de ShopApi.
        options.SetIssuer(new Uri("http://localhost:5124/"));

        // Flows autorisés : password + refresh + authorization code (avec PKCE)
        options.AllowPasswordFlow()
               .AllowRefreshTokenFlow()
               .AllowAuthorizationCodeFlow()
               .RequireProofKeyForCodeExchange();

        // Scopes que le serveur accepte (openid + offline_access sont natifs).
        options.RegisterScopes(Scopes.Email, Scopes.Profile);

        // Durées de vie (on garde nos choix : access court, refresh long)
        options.SetAccessTokenLifetime(TimeSpan.FromMinutes(15));
        options.SetRefreshTokenLifetime(TimeSpan.FromDays(7));

        // Rotation STRICTE : un refresh token consommé est immédiatement rejeté
        // s'il est rejoué (0 s de tolérance). Comme notre ancien système custom.
        options.SetRefreshTokenReuseLeeway(TimeSpan.Zero);

        // Clés de signature/chiffrement — EPHEMERES : régénérées à chaque
        // redémarrage (dev uniquement). En prod : de vrais certificats persistants.
        options.AddEphemeralEncryptionKey()
               .AddEphemeralSigningKey();

        // Access token en JWT lisible (non chiffré) : pratique pour l'inspecter,
        // et pour qu'une autre API (ShopApi) puisse le valider plus tard.
        options.DisableAccessTokenEncryption();

        // Intégration ASP.NET Core : on laisse la requête arriver à NOTRE handler,
        // et on autorise HTTP en dev (sinon OpenIddict exige HTTPS).
        options.UseAspNetCore()
               .EnableTokenEndpointPassthrough()
               .EnableAuthorizationEndpointPassthrough()
               .DisableTransportSecurityRequirement();
    })

    // 3) VALIDATION : accepte les tokens émis par CE serveur (remplace AddJwtBearer)
    .AddValidation(options =>
    {
        options.UseLocalServer();
        options.UseAspNetCore();
    });

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    // Flow OAuth2 "password" : le bouton Authorize de Swagger demandera
    // username / password / client_id et obtiendra le token automatiquement.
    options.AddSecurityDefinition("OAuth2", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.OAuth2,
        Flows = new OpenApiOAuthFlows
        {
            // Flow interactif (Phase 2) : redirige vers la page de login + PKCE.
            AuthorizationCode = new OpenApiOAuthFlow
            {
                AuthorizationUrl = new Uri("/connect/authorize", UriKind.Relative),
                TokenUrl = new Uri("/connect/token", UriKind.Relative),
                Scopes = new Dictionary<string, string>
                {
                    ["openid"] = "Identifiant OpenID",
                    ["email"] = "Adresse email",
                    ["profile"] = "Profil",
                    ["offline_access"] = "Refresh token"
                }
            },
            // Flow direct (Phase 1) : username / password.
            Password = new OpenApiOAuthFlow
            {
                TokenUrl = new Uri("/connect/token", UriKind.Relative),
                Scopes = new Dictionary<string, string>
                {
                    ["openid"] = "Identifiant OpenID",
                    ["email"] = "Adresse email",
                    ["profile"] = "Profil",
                    ["offline_access"] = "Refresh token"
                }
            }
        }
    });

    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "OAuth2"
                }
            },
            new[] { "openid", "email", "profile", "offline_access" }
        }
    });
});
builder.Services.AddAuthorization();

// Autorise le Swagger de ShopApi (origine http://localhost:5050) à appeler
// /connect/token depuis le navigateur (requête cross-origin).
builder.Services.AddCors(options =>
{
    options.AddPolicy("SwaggerClients", policy =>
        policy.WithOrigins("http://localhost:5050")
              .AllowAnyHeader()
              .AllowAnyMethod());
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    await DbSeeder.SeedAsync(scope.ServiceProvider);
    await DbSeeder.SeedOpenIddictClientAsync(scope.ServiceProvider);
    await DbSeeder.SeedShopWebAppClientAsync(scope.ServiceProvider);
}


if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        // Pré-remplit le client_id dans le formulaire Authorize.
        options.OAuthClientId("postman");
        options.OAuthScopes("openid", "email", "profile", "offline_access");
        // PKCE pour le flow authorization code (obligatoire côté serveur).
        options.OAuthUsePkce();
    });
}

app.UseHttpsRedirection();

app.UseCors("SwaggerClients");

app.UseAuthentication();
app.UseAuthorization();

app.MapConnectEndpoints();
app.MapAccountEndpoints();
app.MapUserEndpoints();
app.MapClientEndpoints();
app.MapRoleEndpoints();

// Sert le panneau d'admin compile (admin-ui build -> wwwroot/admin) : endpoint
// direct plutot que UseStaticFiles()+MapFallbackToFile(), qui n'arrivaient
// jamais a servir les fichiers reels ici (le fallback les interceptait tous,
// meme quand le fichier demande existait bel et bien sur le disque). En dev,
// admin-ui tourne separement via Vite (port 5174) avec son propre proxy.
var adminRoot = Path.Combine(builder.Environment.ContentRootPath, "wwwroot", "admin");
app.MapGet("/admin/{*path}", (string? path) =>
{
    var requested = string.IsNullOrEmpty(path) ? "index.html" : path;
    var fullPath = Path.GetFullPath(Path.Combine(adminRoot, requested));

    // Empeche de sortir de adminRoot via un "../" dans l'URL.
    if (!fullPath.StartsWith(Path.GetFullPath(adminRoot), StringComparison.Ordinal))
        return Results.NotFound();

    // Routing cote client (react-router) : un chemin sans fichier correspondant
    // (ex. /admin/users apres un rafraichissement) retombe sur l'index de la SPA.
    if (!File.Exists(fullPath))
        fullPath = Path.Combine(adminRoot, "index.html");

    var contentType = Path.GetExtension(fullPath) switch
    {
        ".html" => "text/html",
        ".js" => "text/javascript",
        ".css" => "text/css",
        ".svg" => "image/svg+xml",
        _ => "application/octet-stream"
    };
    return Results.File(fullPath, contentType);
});

app.Run();