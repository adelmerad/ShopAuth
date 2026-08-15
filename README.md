# ShopAuth

Serveur d'authentification **OAuth2 / OpenID Connect** construit avec ASP.NET Core 8 et **OpenIddict** — projet d'apprentissage réalisé pendant mon stage chez Mobilis (encadrant : M. Ibrahim).

Parti d'une API JWT « maison » (aujourd'hui entièrement retirée), le projet a évolué en véritable serveur d'autorisation standard : il délivre des access tokens JWT signés, des id tokens et des refresh tokens (avec rotation), et d'autres API (comme **ShopApi**) valident ces tokens **sans partager de secret**. ASP.NET Core Identity gère les utilisateurs (hachage, verrouillage de compte).

## Stack

- ASP.NET Core 8 (Minimal API)
- OpenIddict 5 — serveur OAuth2 / OpenID Connect + validation
- ASP.NET Core Identity (`ApplicationUser` personnalisé)
- Entity Framework Core 8 — Code-First + Migrations
- SQL Server 2022 (Docker)
- Swagger / OpenAPI (bouton Authorize câblé sur les flows *authorization code + PKCE* et *password*)

## Renforcements de sécurité

- **Anti-énumération par timing** : pour un email inconnu, `/connect/token` et `/login` exécutent quand même une vérification de hash « à vide » → temps de réponse identique à un mot de passe erroné.
- **Verrouillage de compte** : 5 échecs → compte bloqué 15 min (via `SignInManager`). Actif sur `/connect/token` et `/login`.
- **Rotation stricte des refresh tokens** : un refresh token consommé est immédiatement rejeté s'il est rejoué (leeway = 0).
- **Révocation au changement de mot de passe** : changer son mot de passe invalide tous les refresh tokens existants.

## Structure du projet

```
Entities/     ApplicationUser (+ MustChangePassword)
Data/         ApplicationDbContext (Identity + OpenIddict), DbSeeder
Endpoints/    ConnectEndpoints (/connect/token, /connect/authorize, /login)
Migrations/   Migrations EF Core (Identity + OpenIddict)
```

## Prérequis

- [SDK .NET 8](https://dotnet.microsoft.com/download/dotnet/8.0)
- SQL Server accessible sur `localhost,1433`, par exemple via Docker :

```powershell
docker run -e "ACCEPT_EULA=Y" -e "MSSQL_SA_PASSWORD=VotreMotDePasse123!" -p 1433:1433 --name sqlserver -d mcr.microsoft.com/mssql/server:2022-latest
```

## Installation

1. Restaurer les packages :

```powershell
dotnet restore
```

2. Créer `appsettings.json` à partir du modèle fourni, puis renseigner la chaîne de connexion :

```powershell
Copy-Item appsettings.Example.json appsettings.json
```

3. Appliquer les migrations puis lancer :

```powershell
dotnet ef database update
dotnet run
```

Swagger est disponible sur `/swagger` en environnement Development.

## Utilisateur & clients seedés

Au démarrage, `DbSeeder` crée (idempotent) :

- **Utilisateur de test** : `test@entreprise.com` / `MotDePasseInitial123!` (`MustChangePassword: true`)
- **Client OpenIddict** public `postman` (flows *authorization code* + PKCE, *password*, *refresh_token*)
- **Scope d'API** `shop_api` (dont la *resource* devient l'`aud` des tokens destinés à ShopApi)

## Obtenir un token (OAuth2 *password grant*)

`POST /connect/token` en `application/x-www-form-urlencoded` :

```
grant_type=password
username=test@entreprise.com
password=MotDePasseInitial123!
scope=openid email profile offline_access shop_api
client_id=postman
```

Réponse : `access_token` (JWT signé RS256), `id_token`, `refresh_token`.

Le plus simple : **Swagger → Authorize 🔒** → saisir username/password → cocher les scopes voulus.

## Login interactif (Authorization Code + PKCE)

Le vrai flow SSO : dans Swagger, **Authorize → `authorization_code`** redirige le navigateur vers la page `/login` du serveur (l'app cliente ne voit **jamais** le mot de passe), puis renvoie un `code` échangé — avec **PKCE** — contre les tokens. C'est le mécanisme de « Se connecter avec Google/Microsoft ».

## Endpoints principaux

| Méthode | Route | Rôle |
|---|---|---|
| POST | `/connect/token` | Émission des tokens (*authorization_code*, *password*, *refresh_token*) |
| GET | `/connect/authorize` | Autorisation interactive (Authorization Code + PKCE) |
| GET/POST | `/login` | Page de connexion (formulaire) |
| GET | `/.well-known/openid-configuration` | Découverte OIDC |
| GET | `/.well-known/jwks` | Clés publiques de signature |

## Serveur de ressources (SSO)

Une API tierce (**ShopApi**) valide les tokens émis par ce serveur : elle récupère les clés via `jwks_uri` et n'accepte que les tokens dont `aud` contient `shop_api`. Un **CORS** autorise l'origine de son Swagger (`http://localhost:5050`) à appeler `/connect/token`.

## Notes dev

- **Clés éphémères** : régénérées à chaque redémarrage → les tokens émis avant deviennent invalides. En prod : de vrais certificats persistants.
- Access token **non chiffré** (JWT lisible) pour faciliter le debug et la validation par les serveurs de ressources.
- Le flag `MustChangePassword` existe encore sur `ApplicationUser` (posé à `true` par le seeder) mais **aucun endpoint ne le consomme actuellement** — resterait à rebrancher si un flow de changement de mot de passe est réintroduit.

## Roadmap

- [x] Renforcements sécurité (timing, lockout, rotation, révocation)
- [x] Serveur OpenIddict — *password grant* (Phase 1)
- [x] Resource server : ShopApi valide les tokens (Phase 3)
- [x] Authorization Code + PKCE avec page de login (Phase 2) — consentement auto (implicite)
- [x] Retrait complet de l'ancien système custom `/api/auth/*` (`AuthEndpoints`, `TokenService`, `AuthService`, table `RefreshTokens`, section `Jwt`)
- [ ] Écran de consentement explicite + protection anti-CSRF sur `/login`
- [ ] Register + rôles (role claims)
