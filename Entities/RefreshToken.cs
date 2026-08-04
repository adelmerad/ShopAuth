namespace AuthApiTest.Entities;

public class RefreshToken
{
    public int Id { get; set; }
    public string Token { get; set; } = "";
    public DateTime ExpiresAt { get; set; }
    public bool IsRevoked { get; set; }

    public string UserId { get; set; } = "";
    public ApplicationUser User { get; set; } = null!;
}