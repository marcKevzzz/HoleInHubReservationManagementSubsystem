using System.Security.Cryptography;
using System.Text.Json;
using HoleInHub.Data.Reservations;

namespace HoleInHub.Business.Reservations;

public sealed record AttendanceCommand(string ReservationId, string Action, long ExpectedRevision, string CommandId, string? Reason);
public enum AttendanceResult { Applied, AlreadyApplied, Stale, NotFound, NotEligible }

public sealed partial class ReservationService
{
    public async Task<AttendanceResult> RecordAttendanceAsync(AttendanceCommand command, string actor, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(actor) || string.IsNullOrWhiteSpace(command.ReservationId) || command.ExpectedRevision < 0
            || !Guid.TryParseExact(command.CommandId, "N", out _) || command.Action is not ("check-in" or "check-out")
            || command.Reason?.Length > 500)
        {
            throw new ReservationValidationException("Reload the booking and check the attendance action and notes (up to 500 characters).");
        }
        var reason = command.Reason?.Trim() ?? "";
        var hash = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
        { command.ReservationId, command.Action, command.ExpectedRevision, Actor = actor, Reason = reason })));
        try
        {
            var result = await persistence.RecordAttendanceAsync(command.ReservationId, command.ExpectedRevision, command.CommandId,
                hash, actor, reason, record => AttendanceRules.Decide(record, command.Action, reason,
                    (clock ?? TimeProvider.System).GetUtcNow().UtcDateTime), ct);
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
}
