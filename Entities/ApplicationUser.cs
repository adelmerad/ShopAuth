using Microsoft.AspNetCore.Identity;

namespace ShopAuth.Entities;

public class ApplicationUser : IdentityUser
{
    public bool MustChangePassword { get; set; }
}