using HoleInHub.Business.Accounts;
using HoleInHub.Business.Reservations;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace HoleInHub.Web.Accounts;

public sealed class SmtpOptions
{
    public string Host { get; set; } = "";
    public int Port { get; set; } = 587;
    public bool UseStartTls { get; set; } = true;
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public string From { get; set; } = "";
}
public sealed class AccountEmailOptions
{
    public string PublicBaseUrl { get; set; } = "";
}
public sealed class SmtpEmailSender(IOptions<SmtpOptions> settings, IHostEnvironment environment)
{
    public async Task SendAsync(QueuedEmail email, CancellationToken ct)
    {
        var options = settings.Value;
        if (string.IsNullOrWhiteSpace(options.Host) || string.IsNullOrWhiteSpace(options.From))
        {
            throw new InvalidOperationException("SMTP delivery is not configured.");
        }
        if (!environment.IsDevelopment() && !environment.IsEnvironment("Testing") && !options.UseStartTls)
        {
            throw new InvalidOperationException("Production SMTP requires TLS.");
        }
        var message = new MimeMessage();
        message.From.Add(MailboxAddress.Parse(options.From));
        message.To.Add(MailboxAddress.Parse(email.Recipient));
        message.Subject = email.Subject;
        message.MessageId = $"{email.Id}@holeinhub.local";
        message.Body = new TextPart("plain") { Text = email.Body };
        using var client = new SmtpClient();
        client.Timeout = 30000;
        await client.ConnectAsync(options.Host, options.Port, options.UseStartTls ? SecureSocketOptions.StartTls : SecureSocketOptions.None, ct);
        if (!string.IsNullOrWhiteSpace(options.Username))
        {
            await client.AuthenticateAsync(options.Username, options.Password, ct);
        }
        await client.SendAsync(message, ct);
        await client.DisconnectAsync(true, ct);
    }
}
public sealed class NotificationWorker(IServiceScopeFactory scopes, ILogger<NotificationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<ReservationService>().ProcessPendingAsync(stoppingToken);
                var queue = scope.ServiceProvider.GetRequiredService<NotificationService>();
                for (var count = 0; count < 50; count++)
                {
                    var email = await queue.ClaimAsync(stoppingToken);
                    if (email is null) { break; }
                    try
                    {
                        if (await queue.IsEligibleAsync(email, stoppingToken))
                        {
                            await scope.ServiceProvider.GetRequiredService<SmtpEmailSender>().SendAsync(email, stoppingToken);
                        }
                        await queue.CompleteAsync(email, stoppingToken);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { throw; }
                    catch (Exception)
                    {
                        await queue.RetryAsync(email, stoppingToken);
                        logger.LogWarning("Notification delivery failed for event {EventId}; retry scheduled.", email.Id);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception)
            {
                logger.LogWarning("Reservation notification processing is unavailable; retrying.");
            }
            try { await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }
}
