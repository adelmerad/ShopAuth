using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using ShopAuth.Data;
using ShopAuth.Entities;

namespace ShopAuth.Endpoints;

// Revalide le statut du compte (desactive/suspendu) a chaque appel a l'API
// admin, pas seulement a la connexion. Sans ce filtre, un admin suspendu ou
// desactive apres coup garde un acces complet a /admin/api/* tant que son
// cookie reste valide : le SecurityStamp d'ASP.NET Identity ne revalide qu'a
// intervalles espaces (30 min par defaut), et RequireRole("admin") ne
// regarde que les claims du cookie, jamais l'etat actuel en base.
public class RequireActiveAccountFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;
        var userId = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (userId is not null)
        {
            var userManager = httpContext.RequestServices.GetRequiredService<UserManager<ApplicationUser>>();
            var db = httpContext.RequestServices.GetRequiredService<ApplicationDbContext>();

            var user = await userManager.FindByIdAsync(userId);
            var isActive = user is not null && await AccountStatusChecker.IsActiveAsync(db, user);

            if (!isActive)
            {
                await httpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
                var message = user is not null && user.LockoutEnd is not null && user.LockoutEnd > DateTimeOffset.UtcNow
                    ? AccountStatusChecker.LockedOutMessage(user)
                    : "Ce compte est suspendu.";
                return Results.Json(new { error = message }, statusCode: StatusCodes.Status403Forbidden);
            }
        }

        return await next(context);
    }
}
