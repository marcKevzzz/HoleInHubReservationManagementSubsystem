namespace HoleInHubReservationManagementSubsystem.ViewModels;

public static class StaffNotificationText
{
    public static string Title(string subject)
    {
        var text = subject.ToLowerInvariant();
        if (text.Contains("upcoming")) { return "Upcoming reservation"; }
        if (text.Contains("cancellation review")) { return "Cancellation request needs review"; }
        if (text.Contains("cancelled")) { return "Reservation cancelled"; }
        if (text.Contains("no-show")) { return "Reservation marked no-show"; }
        if (text.Contains("extended")) { return "Reservation extended"; }
        if (text.Contains("request received") || text.Contains("pending request") || text.Contains("overdue review")) { return "Reservation needs review"; }
        if (text.Contains("confirmed")) { return "Reservation confirmed"; }
        if (text.Contains("expired")) { return "Reservation request expired"; }
        if (text.Contains("schedule change")) { return "Reservation schedule updated"; }
        return "Reservation updated";
    }
}
