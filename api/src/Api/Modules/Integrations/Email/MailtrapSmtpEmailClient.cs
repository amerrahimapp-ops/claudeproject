using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace Api.Modules.Integrations.Email;

/// <summary>
/// Real Mailtrap SMTP relay client (MailKit). Used when Email:Provider is
/// "Mailtrap" - see EmailOptions for where the connection settings come
/// from. Selected via the same config-driven Provider switch pattern as
/// Auth (see AuthServiceCollectionExtensions).
/// </summary>
public class MailtrapSmtpEmailClient : IEmailClient
{
    private readonly EmailOptions _options;
    private readonly ILogger<MailtrapSmtpEmailClient> _logger;

    public MailtrapSmtpEmailClient(IOptions<EmailOptions> options, ILogger<MailtrapSmtpEmailClient> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task SendAsync(string toAddress, string subject, string body, CancellationToken cancellationToken = default)
    {
        var message = new MimeMessage();
        message.From.Add(MailboxAddress.Parse(_options.FromAddress));
        message.To.Add(MailboxAddress.Parse(toAddress));
        message.Subject = subject;
        message.Body = new TextPart("plain") { Text = body };

        using var client = new SmtpClient();
        await client.ConnectAsync(_options.Host, _options.Port, SecureSocketOptions.StartTls, cancellationToken);
        await client.AuthenticateAsync(_options.Username, _options.Password, cancellationToken);
        await client.SendAsync(message, cancellationToken);
        await client.DisconnectAsync(true, cancellationToken);

        _logger.LogInformation("Sent email to {ToAddress} with subject {Subject}", toAddress, subject);
    }
}
