namespace Starlight.SDK.Account;

public class Account
{
    public string AccountName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public int Id { get; set; }
    public int PasswordTime { get; set; }
}
