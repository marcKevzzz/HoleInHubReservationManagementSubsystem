using HoleInHub.Business.Reservations;

namespace HoleInHubReservationManagementSubsystem.ViewModels;

public sealed record ReportQuery(DateOnly? From, DateOnly? To, int? Preset, string? Service, string? Status, int Page = 1);

public sealed record ReportsViewModel(DateOnly From, DateOnly To, string? Service, string? Status, int Page,
    int PageCount, int Matching, IReadOnlyList<DashboardBooking> Rows, ReservationReport Report);

public sealed record ReportListViewModel(DateOnly From, DateOnly To, string? Service, string? Status, int Page,
    int PageCount, IReadOnlyList<DashboardBooking> Rows, IReadOnlyDictionary<string, int> Counts);
