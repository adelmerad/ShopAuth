# ShopAuth

Serveur d'authentification **OAuth2 / OpenID Connect** construit avec ASP.NET Core 8 et **OpenIddict** — projet d'apprentissage réalisé pendant mon stage chez Mobilis (encadrant : M. Ibrahim).

Parti d'une API JWT « maison » (aujourd'hui entièrement retirée), le projet a évolué en véritable serveur d'autorisation standard : il délivre des access tokens JWT signés, des id tokens et des refresh tokens (avec rotation), que **ShopWebApp** utilise pour authentifier ses utilisateurs. ASP.NET Core Identity gère les utilisateurs (hachage, verrouillage de compte, rôles).

Le serveur gère aussi les rôles par application, les suspensions de comptes et la déconnexion SSO, et fournit un panneau d'administration React.

## Stack

- ASP.NET Core 8 (Minimal API)
- OpenIddict 5 — serveur OAuth2 / OpenID Connect + validation
- ASP.NET Core Identity (`ApplicationUser` personnalisé)
- Entity Framework Core 8 — Code-First + Migrations
- SQL Server 2022 (Docker)
- Swagger / OpenAPI (bouton Authorize câblé sur les flows *authorization code + PKCE* et *password*)
- React + TypeScript + Vite — panneau d'administration (`admin-ui/`)

## Renforcements de sécurité

- **Anti-énumération par timing** : pour un email inconnu, `/connect/token` et `/login` exécutent quand même une vérification de hash « à vide » → temps de réponse identique à un mot de passe erroné.
- **Verrouillage de compte** : 5 échecs → compte bloqué 15 min (via `SignInManager`). Actif sur `/connect/token` et `/login`.
- **Rotation stricte des refresh tokens** : un refresh token consommé est immédiatement rejeté s'il est rejoué (leeway = 0).
- **Révocation au changement de mot de passe** : changer son mot de passe invalide tous les refresh tokens existants.
- **Statut du compte revérifié** : la suspension est contrôlée au login, sur `/connect/authorize` et sur `/connect/token` (y compris au refresh). Un cookie ou un refresh token déjà valide ne suffit donc pas à garder l'accès.
- **API d'administration protégée** : rôle `admin` obligatoire, statut du compte revérifié à chaque appel (`RequireActiveAccountFilter`) et en-tête `X-Sso-Admin` exigé contre le CSRF (`RequireAdminHeaderFilter`), en plus du cookie `SameSite=Lax`.

## Rôles par application

Un utilisateur peut avoir deux types de rôles :

- **Rôle global** (ASP.NET Identity) : valable pour toutes les applications.
- **Rôle applicatif** (table `UserApplicationRoles`) : valable uniquement pour un `client_id`.

Le claim `role` du token contient l'union des deux pour le client qui demande le token. Par exemple, `employe@entreprise.com` n'a aucun rôle global, mais reçoit `employe` sur `shopwebapp-bff`.

## Suspensions

Une suspension (table `UserSuspensions`) est une période avec une date de début, une date de fin, un motif et une note facultative. Le compte est bloqué uniquement pendant cette période.

Sont refusés : les périodes qui se chevauchent pour un même compte, se suspendre soi-même, et suspendre le dernier administrateur actif.

Le verrouillage temporaire et la désactivation par un administrateur sont distincts : ils utilisent tous deux `LockoutEnd`, mais la désactivation le fixe à `DateTimeOffset.MaxValue` et l'utilisateur reçoit un message différent.

## Structure du projet

```
Entities/     ApplicationUser (+ MustChangePassword), UserApplicationRole, UserSuspension
Data/         ApplicationDbContext (Identity + OpenIddict), DbSeeder, AccountStatusChecker
Endpoints/    ConnectEndpoints (OAuth2/OIDC, /login)
              AccountEndpoints (session du panneau d'admin)
              UserEndpoints, RoleEndpoints, ClientEndpoints (API d'administration)
              RequireActiveAccountFilter, RequireAdminHeaderFilter
Migrations/   Migrations EF Core (Identity + OpenIddict)
admin-ui/     Panneau d'administration React (compilé vers wwwroot/admin, non versionné)
```

## Prérequis

- [SDK .NET 8](https://dotnet.microsoft.com/download/dotnet/8.0)
- [Node.js](https://nodejs.org/) (uniquement pour le panneau d'administration)
- SQL Server accessible sur `localhost,1433`, par exemple via Docker :

```powershell
docker run -e "ACCEPT_EULA=Y" -e "MSSQL_SA_PASSWORD=YOUR_PASSWORD" -p 1433:1433 --name sqlserver -d mcr.microsoft.com/mssql/server:2022-latest
```

## Installation

1. Restaurer les packages :

```powershell
dotnet restore
```

2. Créer `appsettings.json` à partir du modèle fourni, puis renseigner la chaîne de connexion et la section `Bootstrap` (obligatoire, l'application ne démarre pas sans) :

```powershell
Copy-Item appsettings.Example.json appsettings.json
```

3. Appliquer les migrations puis lancer :

```powershell
dotnet ef database update
dotnet run
```

Swagger est disponible sur `/swagger` en environnement Development.

## Panneau d'administration

Le panneau gère les utilisateurs, les rôles, les suspensions et les clients OpenIddict. Il faut le compiler une première fois, car `wwwroot/admin` n'est pas versionné :

```powershell
cd admin-ui
npm install
npm run build
```

Il est ensuite servi par ShopAuth sur `http://localhost:5124/admin/`. La connexion demande un compte ayant le rôle `admin`.

Pour le développement, `npm run dev` lance Vite sur `http://localhost:5174/admin/`, avec un proxy vers ShopAuth.

## Utilisateur & clients seedés

Au démarrage, `DbSeeder` crée (idempotent) :

- **Utilisateur de test** : `test@entreprise.com`, mot de passe `Bootstrap:AdminPassword` (`MustChangePassword: true`), rôle `admin`
- **Utilisateur de test** : `employe@entreprise.com`, mot de passe `Bootstrap:EmployePassword`, rôle applicatif `employe` sur `shopwebapp-bff`
- **Client OpenIddict** public `postman` (flows *authorization code* + PKCE, *password*, *refresh_token*)
- **Client OpenIddict** confidentiel `shopwebapp-bff` (secret `Bootstrap:ShopWebAppClientSecret` + PKCE, dédié à ShopWebApp)

## Obtenir un token (OAuth2 *password grant*)

`POST /connect/token` en `application/x-www-form-urlencoded` :

```
grant_type=password
username=test@entreprise.com
password=<Bootstrap:AdminPassword>
scope=openid email profile offline_access
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
| GET/POST | `/connect/logout` | Déconnexion SSO initiée par le client, puis retour vers `post_logout_redirect_uri` |
| GET | `/.well-known/openid-configuration` | Découverte OIDC |
| GET | `/.well-known/jwks` | Clés publiques de signature |
| POST | `/api/account/login`, `/api/account/logout` | Connexion et déconnexion du panneau d'admin |
| GET | `/api/account/session` | Session courante du panneau d'admin |
| GET | `/admin/{*path}` | Panneau d'administration compilé |

### API d'administration

Ces routes exigent le rôle `admin` et l'en-tête `X-Sso-Admin`.

| Méthode | Route | Rôle |
|---|---|---|
| GET/POST | `/admin/api/users` | Lister, créer des utilisateurs |
| DELETE | `/admin/api/users/{id}` | Supprimer un utilisateur |
| PUT | `/admin/api/users/{id}/roles` | Définir les rôles globaux |
| POST | `/admin/api/users/{id}/app-roles` | Ajouter un rôle applicatif |
| DELETE | `/admin/api/users/{id}/app-roles/{appRoleId}` | Retirer un rôle applicatif |
| POST | `/admin/api/users/{id}/reset-password` | Réinitialiser le mot de passe |
| POST | `/admin/api/users/{id}/disable`, `/enable` | Désactiver, réactiver un compte |
| POST | `/admin/api/users/{id}/suspensions` | Créer une suspension |
| DELETE | `/admin/api/users/{id}/suspensions/{suspensionId}` | Lever une suspension |
| GET/POST | `/admin/api/roles` | Lister, créer des rôles |
| DELETE | `/admin/api/roles/{id}` | Supprimer un rôle |
| GET/POST | `/admin/api/clients` | Lister, créer des clients OpenIddict |
| PUT/DELETE | `/admin/api/clients/{clientId}` | Modifier, supprimer un client |
| POST | `/admin/api/clients/{clientId}/rotate-secret` | Renouveler le secret d'un client |

## Notes dev

- **Clés éphémères** : régénérées à chaque redémarrage → les tokens émis avant deviennent invalides. En prod : de vrais certificats persistants. ShopWebApp met les clés JWKS en cache : après un redémarrage de ShopAuth, redémarrer aussi ShopWebApp.
- Access token **non chiffré** (JWT lisible) pour faciliter le debug et la validation par les serveurs de ressources.
- Le flag `MustChangePassword` existe encore sur `ApplicationUser` (posé à `true` par le seeder) mais **aucun endpoint ne le consomme actuellement** — resterait à rebrancher si un flow de changement de mot de passe est réintroduit.

## Roadmap

- [x] Renforcements sécurité (timing, lockout, rotation, révocation)
- [x] Serveur OpenIddict — *password grant* (Phase 1)
- [x] Authorization Code + PKCE avec page de login (Phase 2) — consentement auto (implicite)
- [x] Retrait complet de l'ancien système custom `/api/auth/*` (`AuthEndpoints`, `TokenService`, `AuthService`, table `RefreshTokens`, section `Jwt`)
- [x] Claim `role` dans les tokens (rôle `admin` seedé)
- [x] Client confidentiel dédié `shopwebapp-bff` (au lieu de partager `postman` avec Swagger)
- [x] Rôles par application (`UserApplicationRoles`)
- [x] Suspensions de comptes par période (`UserSuspensions`)
- [x] API d'administration et panneau React (`admin-ui`)
- [x] Déconnexion initiée par le client (`/connect/logout`)
- [ ] Écran de consentement explicite + protection anti-CSRF sur `/login`
- [ ] Register
