# AuthApiTest

API d'authentification JWT construite avec ASP.NET Core 8 — projet d'apprentissage réalisé pendant mon stage chez Mobilis (encadrant : M. Ibrahim).

Implémente un cycle d'authentification complet : login par email/mot de passe, access tokens JWT courts, refresh tokens avec rotation, déconnexion par révocation, et changement de mot de passe obligatoire au premier login.

## Stack

- ASP.NET Core 8 (Minimal API)
- Entity Framework Core 8 — Code-First + Migrations
- ASP.NET Core Identity (`ApplicationUser` personnalisé)
- JWT Bearer (HMAC-SHA256)
- SQL Server 2022 (Docker)
- Swagger / OpenAPI (avec bouton Authorize pour tester les endpoints protégés)

## Structure du projet

```
Entities/     ApplicationUser (+ MustChangePassword), RefreshToken
Data/         ApplicationDbContext (IdentityDbContext), DbSeeder
DTOs/         Records de requête/réponse (LoginRequest, AuthResponse, ...)
Services/     ITokenService/TokenService, IAuthService/AuthService
Endpoints/    AuthEndpoints — groupe de routes /api/auth
Migrations/   Migrations EF Core
```

Les services sont injectés derrière des interfaces (découplage) : la génération des tokens est isolée dans `TokenService`, la logique métier dans `AuthService`, les endpoints ne font que traduire HTTP ↔ services.

## Prérequis

- [SDK .NET 8](https://dotnet.microsoft.com/download/dotnet/8.0)
- SQL Server accessible sur `localhost,1433`, par exemple via Docker :

```powershell
docker run -e "ACCEPT_EULA=Y" -e "MSSQL_SA_PASSWORD=VotreMotDePasse123!" -p 1433:1433 --name sqlserver -d mcr.microsoft.com/mssql/server:2022-latest
```

## Installation

1. Cloner le dépôt puis restaurer les packages :

```powershell
dotnet restore
```

2. Configurer `appsettings.json` (chaîne de connexion + section JWT) :

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=localhost,1433;Database=AuthTestDb;User Id=sa;Password=***;TrustServerCertificate=True"
  },
  "Jwt": {
    "Key": "une-cle-secrete-de-32-caracteres-minimum",
    "Issuer": "AuthApiTest",
    "Audience": "AuthApiTestClient"
  }
}
```

> La clé JWT doit faire au moins 32 caractères (exigence HMAC-SHA256). Ne jamais committer de vrais secrets.

3. Appliquer les migrations :

```powershell
dotnet ef database update
```

4. Lancer :

```powershell
dotnet run
```

Swagger est disponible sur `/swagger` en environnement Development.

## Utilisateur de test

Au démarrage, `DbSeeder` crée un compte de démonstration s'il n'existe pas (idempotent) :

- Email : `test@entreprise.com`
- Mot de passe : `MotDePasseInitial123!`
- `MustChangePassword: true` → simule un compte créé par un administrateur, dont le mot de passe doit être changé au premier login

## Endpoints

Toutes les routes sont sous `/api/auth`.

| Méthode | Route | Jeton requis | Body |
|---|---|---|---|
| POST | `/login` | Non | `{ "email": "", "password": "" }` |
| POST | `/refresh-token` | Non | `{ "refreshToken": "" }` |
| POST | `/logout` | Non | `{ "refreshToken": "" }` |
| POST | `/change-password` | **Oui** (Bearer) | `{ "currentPassword": "", "newPassword": "" }` |

`login` et `refresh-token` renvoient :

```json
{
  "accessToken": "eyJhbGciOi...",
  "refreshToken": "kX9f2mQ8...",
  "mustChangePassword": true
}
```

## Choix de sécurité

- **Access token court (15 min)** : un JWT signé est irrévocable ; sa courte durée de vie limite les dégâts en cas de vol.
- **Refresh token long (7 jours), stocké en base** : c'est lui qu'on peut révoquer (`IsRevoked`) — le logout révoque le refresh token.
- **Rotation** : chaque refresh token est à usage unique ; le rejouer renvoie 401.
- **Anti-énumération** : le login répond de façon identique (401) pour un email inconnu et un mot de passe erroné.
- **Hachage** : les mots de passe passent exclusivement par `UserManager` (jamais de manipulation directe du hash).
- **Validation complète** : signature, expiration, issuer et audience sont vérifiés à chaque requête par le middleware JwtBearer.

## Scénario de test (Swagger)

1. `POST /login` avec les identifiants du compte seedé → 200, `mustChangePassword: true`
2. Bouton **Authorize** 🔒 → coller l'`accessToken`
3. `POST /change-password` → 200 ; un nouveau login renvoie `mustChangePassword: false`
4. `POST /refresh-token` → 200 ; rejouer le même refresh token → 401 (rotation)
5. `POST /logout` puis refresh avec ce token → 401 (révoqué)

## Roadmap

- [x] Login / logout / refresh avec rotation
- [x] Changement de mot de passe forcé au premier login
- [ ] Endpoint d'inscription (register) + rôles (role claims)
- [ ] Intégration de l'authentification dans ShopApi
- [ ] Serveur SSO avec OpenIddict (OAuth2 / OpenID Connect, multi-applications)
