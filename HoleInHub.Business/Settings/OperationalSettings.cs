using System.Net.Mail;
using System.Security.Cryptography;
using System.Text.Json;
using HoleInHub.Business.Reservations;
using HoleInHub.Data.Settings;
using Microsoft.Extensions.Options;
namespace HoleInHub.Business.Settings;
public sealed class SettingsEdit
{
    public long Revision { get; set; } = -1;
    public string CommandId { get; set; } = Guid.NewGuid().ToString("N");
    public bool OnlineEnabled { get; set; }
    public bool DurationsApproved { get; set; }
    public int ReviewStartMinutes { get; set; }
    public int ReviewEndMinutes { get; set; } = 1440;
    public int[] ReviewDays { get; set; } = [];
    public string[] StaffEmails { get; set; } = [];
    public string VenueName { get; set; } = "Hole In Hub Golf & Lounge";
    public string ContactEmail { get; set; } = "";
    public string ContactPhone { get; set; } = "";
    public string BookingPolicy { get; set; } = "";
    public string PrivacyPolicy { get; set; } = "";
    public string VenueRules { get; set; } = "";
    public bool PoliciesPublished { get; set; }
    public string UpdatedBy { get; set; } = "";
    public DateTime? UpdatedUtc { get; set; }
}
public sealed class OperationalSettings(SystemSettingsPersistence persistence, IOptions<ReservationPolicy> defaults, TimeProvider clock)
{
    private SettingsEdit? cached;
    public async Task<SettingsEdit> GetAsync(CancellationToken ct)
    {
        if (cached is not null) { return cached; }
        var row = await persistence.GetAsync(ct);
        cached = row is null ? new SettingsEdit { OnlineEnabled = defaults.Value.Approved, DurationsApproved = defaults.Value.DurationsApproved,
            ReviewStartMinutes = (int)defaults.Value.ReviewStart.TotalMinutes, ReviewEndMinutes = (int)defaults.Value.ReviewEnd.TotalMinutes,
            ReviewDays = defaults.Value.ReviewDays, StaffEmails = defaults.Value.StaffEmails }
            : new SettingsEdit { Revision = row.Revision, OnlineEnabled = row.OnlineEnabled, DurationsApproved = row.DurationsApproved,
                ReviewStartMinutes = row.ReviewStartMinutes, ReviewEndMinutes = row.ReviewEndMinutes, ReviewDays = row.ReviewDays,
                StaffEmails = row.StaffEmails, VenueName = row.VenueName, ContactEmail = row.ContactEmail, ContactPhone = row.ContactPhone,
                BookingPolicy = row.BookingPolicy, PrivacyPolicy = row.PrivacyPolicy, VenueRules = row.VenueRules, PoliciesPublished = row.PoliciesPublished,
                UpdatedBy = row.UpdatedBy, UpdatedUtc = row.UpdatedUtc };
        return cached;
    }
    public static ReservationPolicy Apply(SettingsEdit edit, ReservationPolicy deployment) => new()
    {
        Approved = edit.OnlineEnabled, DurationsApproved = edit.DurationsApproved, ReviewStart = TimeSpan.FromMinutes(edit.ReviewStartMinutes),
        ReviewEnd = TimeSpan.FromMinutes(edit.ReviewEndMinutes), ReviewDays = edit.ReviewDays, StaffEmails = edit.StaffEmails,
        DemoPaymentsEnabled = deployment.DemoPaymentsEnabled
    };
    public async Task<ReservationPolicy> PolicyAsync(CancellationToken ct) => Apply(await GetAsync(ct), defaults.Value);
    public static void Validate(SettingsEdit edit)
    {
        if (edit.Revision < -1 || !Guid.TryParseExact(edit.CommandId, "N", out _) || string.IsNullOrWhiteSpace(edit.VenueName) || edit.VenueName.Length > 120
            || edit.ContactEmail.Length > 254 || edit.ContactPhone.Length > 40 || edit.ContactPhone.Length > 0 && !HoleInHub.Business.Accounts.ContactRules.IsPhone(edit.ContactPhone) || edit.ContactEmail.Length > 0 && !MailAddress.TryCreate(edit.ContactEmail, out _)
            || edit.BookingPolicy.Length > 12000 || edit.PrivacyPolicy.Length > 12000 || edit.VenueRules.Length > 12000)
        { throw new ArgumentException("Check the venue details and policy text limits."); }
        if (edit.ReviewStartMinutes is < 0 or >= 1440 || edit.ReviewEndMinutes is < 1 or > 1440 || edit.ReviewStartMinutes >= edit.ReviewEndMinutes
            || edit.ReviewDays.Length > 7 || edit.ReviewDays.Any(day => day is < 0 or > 6) || edit.ReviewDays.Distinct().Count() != edit.ReviewDays.Length
            || edit.StaffEmails.Length > 20 || edit.StaffEmails.Any(email => email.Length > 254 || !MailAddress.TryCreate(email, out var address) || address.Address != email))
        { throw new ArgumentException("Choose valid review hours, days, and up to 20 email recipients."); }
        if (edit.OnlineEnabled)
        {
            try { Apply(edit, new()).ValidateCoverage(DateTimeOffset.UtcNow); }
            catch (ReservationValidationException) { throw new ArgumentException("To open online requests, approve the duration policy and configure review days, staffed hours, and at least one alert recipient."); }
        }
        if (edit.PoliciesPublished && (string.IsNullOrWhiteSpace(edit.BookingPolicy) || string.IsNullOrWhiteSpace(edit.PrivacyPolicy) || string.IsNullOrWhiteSpace(edit.VenueRules)))
        { throw new ArgumentException("Complete all three policies before publishing."); }
    }
    public async Task SaveAsync(SettingsEdit edit, string actor, CancellationToken ct)
    {
        Validate(edit);
        if (string.IsNullOrWhiteSpace(actor)) { throw new ArgumentException("Sign in before saving settings."); }
        var hash = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { edit, actor })));
        await persistence.SaveAsync(new SystemSettingsRecord { CommandId = edit.CommandId, CommandHash = hash, OnlineEnabled = edit.OnlineEnabled,
            DurationsApproved = edit.DurationsApproved, ReviewStartMinutes = edit.ReviewStartMinutes, ReviewEndMinutes = edit.ReviewEndMinutes,
            ReviewDays = edit.ReviewDays.Order().ToArray(), StaffEmails = edit.StaffEmails.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            VenueName = edit.VenueName.Trim(), ContactEmail = edit.ContactEmail.Trim(), ContactPhone = edit.ContactPhone.Trim(),
            BookingPolicy = edit.BookingPolicy.Trim(), PrivacyPolicy = edit.PrivacyPolicy.Trim(), VenueRules = edit.VenueRules.Trim(), PoliciesPublished = edit.PoliciesPublished,
            UpdatedBy = actor, UpdatedUtc = clock.GetUtcNow().UtcDateTime }, edit.Revision, ct);
        cached = null;
    }
}
