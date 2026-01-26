namespace Local_Network_Messenger.Models
{
    public sealed record ValidationError(string Field, string Message);

    public sealed record LoginRequest(string Username, string Password);

    public sealed record RegisterRequest(string Username, string DisplayName, string Password, string ConfirmPassword);

    public sealed record RenameRequest(string NewUsername);

    public sealed record DisplayNameRequest(string DisplayName);

    public sealed record UserProfile(string Username, string DisplayName);
}
