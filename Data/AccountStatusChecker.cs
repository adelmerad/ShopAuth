using Microsoft.EntityFrameworkCore;
using ShopAuth.Entities;

namespace ShopAuth.Data;

// Un cookie ou un refresh token deja valides ne doivent pas suffire a garder
// l'acces : cette verification est appelee au login, a /connect/authorize
// ET a /connect/token (refresh), pas seulement a la premiere connexion.
public static class AccountStatusChecker
{
    public static async Task<bool> IsSuspendedAsync(ApplicationDbContext db, string userId)
    {
        var now = DateTimeOffset.UtcNow;
        return await db.UserSuspensions.AnyAsync(s =>
            s.UserId == userId && s.StartsAt <= now && now <= s.EndsAt);
    }

    // Un compte est-il actuellement bloque (desactive ou suspendu) ? Utilise
    // par le filtre qui revalide le statut a chaque appel a l'API admin, pas
    // seulement a la connexion.
    public static async Task<bool> IsActiveAsync(ApplicationDbContext db, ApplicationUser user)
    {
        if (user.LockoutEnd is not null && user.LockoutEnd > DateTimeOffset.UtcNow)
            return false;

        return !await IsSuspendedAsync(db, user.Id);
    }

    // Message distinct entre verrouillage temporaire (echecs de connexion
    // repetes, ASP.NET Identity) et desactivation par un administrateur
    // (meme mecanisme LockoutEnd, mais fixe a DateTimeOffset.MaxValue) :
    // sinon un utilisateur qui s'est juste trompe de mot de passe croirait
    // que son compte a ete coupe definitivement.
    public static string LockedOutMessage(ApplicationUser user)
    {
        if (user.LockoutEnd == DateTimeOffset.MaxValue)
            return "Ce compte a été désactivé.";

        var minutes = user.LockoutEnd is null
            ? (int?)null
            : Math.Max(1, (int)Math.Ceiling((user.LockoutEnd.Value - DateTimeOffset.UtcNow).TotalMinutes));

        return minutes is null
            ? "Trop de tentatives échouées. Réessayez plus tard."
            : $"Trop de tentatives échouées. Réessayez dans {minutes} minute{(minutes > 1 ? "s" : "")}.";
    }
}
