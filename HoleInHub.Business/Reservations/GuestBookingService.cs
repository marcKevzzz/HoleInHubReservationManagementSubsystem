using System.Security.Cryptography;
using System.Text;
using HoleInHub.Business.Accounts;
using HoleInHub.Data.Reservations;

namespace HoleInHub.Business.Reservations;

public sealed class GuestBookingService(EmailVerificationPersistence persistence, TimeProvider? clock = null)
{
    private readonly EmailVerificationService codes = new(persistence, clock);
    public static string Hash(string value) => EmailVerificationService.Hash(value);
    public static string CodeHash(string session, string email, string code) => EmailVerificationService.CodeHash(session, email, code);
    public static string AccessToken(string session, string key) => Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(session), Encoding.UTF8.GetBytes(key)));
    public async Task IssueAsync(string session, string email, string codeHash, string encryptedBody, CancellationToken ct, string? protectedDraft = null)
    {
        await codes.IssueAsync(session, email, codeHash, encryptedBody, "Your booking verification code", "GuestBooking", ct, protectedDraft);
    }
    public async Task VerifyAsync(string session, string email, string code, CancellationToken ct)
    {
        await codes.VerifyAsync(session, email, code, ct);
    }
    public async Task<GuestBookingDraftDto?> DraftAsync(string session, CancellationToken ct)
    {
        var record = await persistence.FindAsync(Hash(session), ct);
        return record?.ProtectedContext != null && record.ExpiresUtc > (clock ?? TimeProvider.System).GetUtcNow().UtcDateTime
            ? new(record.Email, record.ProtectedContext, record.VerifiedUtc != null, record.ExpiresUtc, record.SentUtc.AddMinutes(1)) : null;
    }
    public async Task<string?> VerifiedEmailAsync(string session, CancellationToken ct)
    {
        var record = await persistence.FindAsync(Hash(session), ct);
        return record is { VerifiedUtc: not null, ConsumedBySubmissionKey: null } && record.ExpiresUtc > (clock ?? TimeProvider.System).GetUtcNow().UtcDateTime ? record.Email : null;
    }
}
