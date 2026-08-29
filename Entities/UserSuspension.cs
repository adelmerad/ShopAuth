namespace ShopAuth.Entities;

public enum SuspensionReason
{
    Conge,
    Disciplinaire,
    Autre
}

// Suspension temporaire d'un compte : une fenetre de dates, pas un simple
// booleen. Verifiee "a la volee" (StartsAt <= maintenant <= EndsAt) a chaque
// connexion, jamais materialisee sur l'utilisateur - donc la levee de la
// suspension est automatique des que EndsAt est depasse, sans rien a faire.
public class UserSuspension
{
    public int Id { get; set; }

    public string UserId { get; set; } = null!;
    public ApplicationUser User { get; set; } = null!;

    public DateTimeOffset StartsAt { get; set; }
    public DateTimeOffset EndsAt { get; set; }

    public SuspensionReason Reason { get; set; }
    public string? Note { get; set; }
}
