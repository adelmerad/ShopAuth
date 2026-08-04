using AuthApiTest.Entities;

namespace AuthApiTest.Services
{
    public interface ITokenService
    {
        string GenerateAccessToken(ApplicationUser user);
        string GenerateRefreshToken();
    }
}
