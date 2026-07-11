namespace Api.Modules.Integrations.Email;

public interface IEmailClient
{
    Task SendAsync(string toAddress, string subject, string body, CancellationToken cancellationToken = default);
}
