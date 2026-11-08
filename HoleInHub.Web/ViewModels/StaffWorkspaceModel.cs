using HoleInHub.Business.Reservations;

namespace HoleInHubReservationManagementSubsystem.ViewModels;

public sealed record StaffWorkspaceModel(DateOnly Day, DateOnly Today, IReadOnlyList<StaffScheduleBay> Bays,
    StaffBookingLookup Lookup, string? DateError, StaffBookingForm BookingForm, StaffBlockForm? BlockForm = null);

public sealed record StaffBookingLookup(DateOnly Day, string Search, IReadOnlyList<StaffScheduleEntry> Matches, string? Error);

public sealed record StaffBookingLinkModel(StaffScheduleEntry Entry, DateOnly Day, string Location, string Css, string? Style = null, bool Wide = false);
