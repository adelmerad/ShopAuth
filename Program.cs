using AuthApiTest.Endpoints;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Validation.AspNetCore;
using Microsoft.OpenApi.Models;
using AuthApiTest.Data;
using AuthApiTest.Entities;
using AuthApiTest.Services;
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
builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/login";
    options.ExpireTimeSpan = TimeSpan.FromMinutes(30); // session d'auth courte
    options.SlidingExpiration = false;                 // expiration FERME
});

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

        // Flows autorisés : password + refresh + authorization code (avec PKCE)
        options.AllowPasswordFlow()
               .AllowRefreshTokenFlow()
               .AllowAuthorizationCodeFlow()
               .RequireProofKeyForCodeExchange();

        // Scopes que le serveur accepte (openid + offline_access sont natifs).
        options.RegisterScopes(Scopes.Email, Scopes.Profile, "shop_api");

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
                    ["offline_access"] = "Refresh token",
                    ["shop_api"] = "Accès à ShopApi"
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
                    ["offline_access"] = "Refresh token",
                    ["shop_api"] = "Accès à ShopApi"
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
builder.Services.AddScoped<ITokenService, TokenService>();
builder.Services.AddScoped<IAuthService, AuthService>();

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
    await DbSeeder.SeedApiScopeAsync(scope.ServiceProvider);
    await DbSeeder.SeedOpenIddictClientAsync(scope.ServiceProvider);
}


if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        // Pré-remplit le client_id dans le formulaire Authorize.
        options.OAuthClientId("postman");
        options.OAuthScopes("openid", "email", "profile", "offline_access", "shop_api");
        // PKCE pour le flow authorization code (obligatoire côté serveur).
        options.OAuthUsePkce();
    });
}

app.UseHttpsRedirection();

app.UseCors("SwaggerClients");

app.UseAuthentication();
app.UseAuthorization();

app.MapAuthEndpoints();
app.MapConnectEndpoints();

app.Run();