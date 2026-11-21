using System.Security.Cryptography;
using System.Text.Json;
using HoleInHub.Data.Reservations;

namespace HoleInHub.Business.Reservations;

public sealed record AttendanceCommand(string ReservationId, string Action, long ExpectedRevision, string CommandId, string? Reason,
    string? CorrectedStatus = null, DateTime? ArrivalUtc = null, DateTime? DepartureUtc = null, int ExtensionMinutes = 0);
public enum AttendanceResult { Applied, AlreadyApplied, Stale, NotFound, NotEligible }

public sealed partial class ReservationService
{
    public async Task<AttendanceResult> RecordAttendanceAsync(AttendanceCommand command, string actor, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(actor) || string.IsNullOrWhiteSpace(command.ReservationId) || command.ExpectedRevision < 0
            || !Guid.TryParseExact(command.CommandId, "N", out _) || command.Action is not ("check-in" or "check-out" or "no-show" or "correct" or "extend" or "end-session" or "complete-checkout")
            || command.Reason?.Length > 500)
        {
            throw new ReservationValidationException("Reload the booking and check the attendance action and notes (up to 500 characters).");
        }
        var reason = command.Reason?.Trim() ?? "";
        if (command.Action is "no-show" or "correct" or "extend" && !await persistence.AttendanceOperationsInstalledAsync(ct))
        { throw new ReservationValidationException("Apply database migration 020 before using no-show, attendance corrections or extensions."); }
        // Keep existing attendance command hashes valid across this upgrade.
        var payload = command.Action is "check-in" or "check-out"
            ? JsonSerializer.SerializeToUtf8Bytes(new { command.ReservationId, command.Action, command.ExpectedRevision, Actor = actor, Reason = reason })
            : JsonSerializer.SerializeToUtf8Bytes(new
            {
                command.ReservationId,
                command.Action,
                command.ExpectedRevision,
                command.CorrectedStatus,
                command.ArrivalUtc,
                command.DepartureUtc,
                command.ExtensionMinutes,
                Actor = actor,
                Reason = reason
            });
        var hash = Convert.ToHexString(SHA256.HashData(payload));
        try
        {
            var result = await persistence.RecordAttendanceAsync(command.ReservationId, command.ExpectedRevision, command.CommandId,
                hash, actor, reason, record => DecideStaffOperation(record, command, reason,
                    (clock ?? TimeProvider.System).GetUtcNow().UtcDateTime), ct, validateSession: ValidateChangedSession);
            return result switch
            {
                AttendanceWriteResult.Applied => AttendanceResult.Applied,
                AttendanceWriteResult.AlreadyApplied => AttendanceResult.AlreadyApplied,
                AttendanceWriteResult.Stale => AttendanceResult.Stale,
                AttendanceWriteResult.NotFound => AttendanceResult.NotFound,
                _ => AttendanceResult.NotEligible
            };
        }
        catch (AllocationConflictException error) { throw new ReservationValidationException(error.Message); }
    }

    private AttendanceChange? DecideStaffOperation(ReservationRecord record, AttendanceCommand command, string reason, DateTime now) => command.Action switch
    {
        "correct" when record.Status == AttendanceRules.AwaitingSettlement || record.CheckoutCompletedUtc.HasValue
            || (policy.Value.RequirePosSettlement && record.Status != "Completed" && command.CorrectedStatus == "Completed") => null,
        "correct" => AttendanceCorrectionRules.Decide(record, command.CorrectedStatus, command.ArrivalUtc, command.DepartureUtc, reason, now),
        "extend" => SessionExtensionRules.Decide(record, command.ExtensionMinutes, reason, now),
        _ => AttendanceRules.DecideCheckout(record, command.Action, reason, now, policy.Value.RequirePosSettlement)
    };
}
