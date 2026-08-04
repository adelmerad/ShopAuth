using AuthApiTest.Entities;
using Microsoft.AspNetCore.Identity;

namespace AuthApiTest.Data;

public static class DbSeeder
{
    public static async Task SeedAsync(IServiceProvider services)
    {
        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();

        const string email = "test@entreprise.com";

        // Déjà présent ? On ne fait rien (idempotent)
        if (await userManager.FindByEmailAsync(email) is not null)
            return;

        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            MustChangePassword = true
        };

        var result = await userManager.CreateAsync(user, "MotDePasseInitial123!");

        if (!result.Succeeded)
        {
            var errors = string.Join(", ", result.Errors.Select(e => e.Description));
            throw new Exception($"Échec du seed : {errors}");
        }
    }
}