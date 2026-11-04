using HoleInHub.Business.Reservations;
namespace HoleInHubReservationManagementSubsystem.ViewModels;

public sealed record BookingRecoveryState(DateTimeOffset SavedUtc, SubmitReservationRequest Request);
public sealed record ServiceUnavailableViewModel(string Message, string RetryUrl, bool BookingSaved, string CorrelationId);
