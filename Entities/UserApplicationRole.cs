using Microsoft.AspNetCore.Identity;

namespace ShopAuth.Entities;

// Table de liaison User <-> Application (client_id OAuth) <-> Role.
// Un IdentityRole du catalogue global (ex. "Employe") peut etre attribue a un
// utilisateur seulement pour une application precise, sans lui donner ce role
// ailleurs. Le role "admin" reste global (verifie via AspNetUserRoles, pas ici).
public class UserApplicationRole
{
    public int Id { get; set; }

    public string UserId { get; set; } = null!;
    public ApplicationUser User { get; set; } = null!;

    // Le ClientId public OpenIddict (ex. "shopwebapp-bff"), pas une cle interne.
    public string ClientId { get; set; } = null!;

    public string RoleId { get; set; } = null!;
    public IdentityRole Role { get; set; } = null!;
}
