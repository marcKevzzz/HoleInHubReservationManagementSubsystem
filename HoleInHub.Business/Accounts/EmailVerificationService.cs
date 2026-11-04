using System.Security.Cryptography;
using System.Text;
using HoleInHub.Business.Reservations;
using HoleInHub.Data.Reservations;

namespace HoleInHub.Business.Accounts;

public sealed record EmailChallenge(string Email, string? ProtectedContext, DateTime ExpiresUtc, DateTime ResendAfterUtc, bool Verified);

public sealed class EmailVerificationService(EmailVerificationPersistence persistence, TimeProvider? clock = null)
{
    public static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    public static string CodeHash(string scope, string email, string code) => Hash(scope + ":" + email + ":" + code);
    public async Task IssueAsync(string scope, string email, string codeHash, string encryptedBody, string subject, string purpose, CancellationToken ct, string? protectedContext = null)
    {
        if (email.Length > 254 || !System.Net.Mail.MailAddress.TryCreate(email, out var address) || address.Address != email)
        {
            throw new ReservationValidationException("Enter a valid email address.");
        }

        var now = (clock ?? TimeProvider.System).GetUtcNow().UtcDateTime;
        if (!await persistence.IssueAsync(new GuestVerificationRecord { Id = Hash(scope), Purpose = purpose, Email = email, CodeHash = codeHash, ProtectedContext = protectedContext, SentUtc = now, ExpiresUtc = now.AddMinutes(10) },
            new OutboxRecord { Id = Guid.NewGuid().ToString("N"), Recipient = email, Subject = subject, Body = encryptedBody, DueUtc = now }, ct, purpose == "GuestRecovery" ? new EmailDeliveryQuota(Hash(email), 5, TimeSpan.FromHours(1), TimeSpan.FromMinutes(1)) : null))
        {
            throw new ReservationValidationException(purpose == "GuestRecovery" ? "Please wait before requesting another code. You can request up to five codes per hour." : "Wait one minute before requesting another code.");
        }
    }
    public async Task VerifyAsync(string scope, string email, string code, CancellationToken ct, string? purpose = null)
    {
        if (code.Length != 6 || !code.All(char.IsAsciiDigit) || !await persistence.VerifyAsync(Hash(scope), email, CodeHash(scope, email, code), (clock ?? TimeProvider.System).GetUtcNow().UtcDateTime, ct, purpose, consume: purpose == "GuestRecovery"))
        {
            throw new ReservationValidationException("This code is invalid or expired. Request another code after five unsuccessful attempts.");
        }
    }
    public async Task<EmailChallenge?> ReadAsync(string scope, CancellationToken ct)
    {
        var record = await persistence.FindAsync(Hash(scope), ct);
        return record is not null ? new(record.Email, record.ProtectedContext, record.ExpiresUtc, record.SentUtc.AddMinutes(1), record.VerifiedUtc != null) : null;
    }
}
