using AuthApiTest.Endpoints;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Validation.AspNetCore;
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
        // Endpoint OAuth2 standard pour obtenir un token
        options.SetTokenEndpointUris("connect/token");

        // Flows autorisés en Phase 1 : password + refresh token
        options.AllowPasswordFlow()
               .AllowRefreshTokenFlow();

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
    options.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
        Scheme = "Bearer",
        BearerFormat = "JWT",
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Description = "Colle ton access token ici (sans le mot 'Bearer')"
    });
    options.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
    {
        {
            new Microsoft.OpenApi.Models.OpenApiSecurityScheme
            {
                Reference = new Microsoft.OpenApi.Models.OpenApiReference
                {
                    Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});
builder.Services.AddScoped<ITokenService, TokenService>();
builder.Services.AddScoped<IAuthService, AuthService>();

builder.Services.AddAuthorization();
var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    await DbSeeder.SeedAsync(scope.ServiceProvider);
    await DbSeeder.SeedOpenIddictClientAsync(scope.ServiceProvider);
}


if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.MapAuthEndpoints();
app.MapConnectEndpoints();

app.Run();