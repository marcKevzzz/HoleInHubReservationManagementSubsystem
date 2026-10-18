using System.ComponentModel.DataAnnotations;

namespace HoleInHub.Data.Configuration;

public sealed class MongoDbOptions
{
    public const string SectionName = "MongoDb";

    [Required(ErrorMessage = "MongoDB connection string is required.")]
    public string ConnectionString { get; set; } = string.Empty;

    [Required(ErrorMessage = "MongoDB database name is required.")]
    public string DatabaseName { get; set; } = string.Empty;

    public string? ReplicaSetName { get; set; }

    public int ServerSelectionTimeoutMs { get; set; } = 5000;

    public int SocketTimeoutMs { get; set; } = 10000;

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString))
        {
            throw new InvalidOperationException("MongoDbOptions.ConnectionString must not be empty.");
        }

        if (string.IsNullOrWhiteSpace(DatabaseName))
        {
            throw new InvalidOperationException("MongoDbOptions.DatabaseName must not be empty.");
        }
    }
}
