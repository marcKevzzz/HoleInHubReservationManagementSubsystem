namespace HoleInHub.Business.Services;

public sealed class ServiceUnavailableException() : Exception("We cannot reach the booking service right now. Please try again shortly.");

public static class ServiceAvailability
{
    public static ServiceUnavailableException? Translate(Exception error) => error as ServiceUnavailableException ?? (HoleInHub.Data.DataAvailability.IsUnavailable(error) ? new ServiceUnavailableException() : null);
}
