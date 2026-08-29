using Microsoft.EntityFrameworkCore;

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
}
