using MongoDB.Driver;
namespace HoleInHub.Data;

public static class DataAvailability
{
    public static bool IsUnavailable(Exception error) => error is TimeoutException or MongoConnectionException or MongoExecutionTimeoutException or MongoAuthenticationException || error.InnerException is not null && IsUnavailable(error.InnerException);
}
