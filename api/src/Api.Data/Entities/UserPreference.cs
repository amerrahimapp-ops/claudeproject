namespace Api.Data.Entities;

public class UserPreference
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public User User { get; set; } = null!;

    /// <summary>JSON blob of per-channel notification preferences.</summary>
    public string NotificationPrefs { get; set; } = null!;

    public string DefaultView { get; set; } = null!;
    public ThemePreference Theme { get; set; }
}
