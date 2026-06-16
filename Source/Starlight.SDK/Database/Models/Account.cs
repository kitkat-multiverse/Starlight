namespace Starlight.SDK.Database.Models;

public sealed class Account
{
    public uint Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public int PasswordTime { get; set; }
}
