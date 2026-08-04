using Microsoft.AspNetCore.Identity;

namespace AuthApiTest.Entities;

public class ApplicationUser : IdentityUser
{
    public bool MustChangePassword { get; set; }
}