namespace Api.Modules.Integrations.Email;

/// <summary>
/// Binds the "Email:Mailtrap" config section. Host/Port/Username/Password
/// come from Mailtrap's SMTP relay settings (username is always the literal
/// "api"; password is your Mailtrap API token). Set locally via
/// appsettings.Development.json (git-ignored), in CI/production via GitHub
/// Actions encrypted secrets - see CLAUDE.md's secrets convention.
/// </summary>
public class EmailOptions
{
    public const string SectionName = "Email:Mailtrap";

    public string Host { get; set; } = null!;
    public int Port { get; set; }
    public string Username { get; set; } = null!;
    public string Password { get; set; } = null!;
    public string FromAddress { get; set; } = "noreply@capacity-request-system.local";
}
