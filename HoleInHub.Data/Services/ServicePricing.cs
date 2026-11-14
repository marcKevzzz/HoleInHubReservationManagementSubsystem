using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace HoleInHub.Data.Services;

public sealed record ServicePricing
{
    [BsonRepresentation(BsonType.Decimal128)] public decimal BaseRate { get; init; }
    public int ClassCount { get; init; }
    [BsonRepresentation(BsonType.Decimal128)] public decimal WholeDayRate { get; init; }
    public HourlyRateBand[] Bands { get; init; } = [];
    public RentalRateOverride[] RentalOverrides { get; init; } = [];
}

public sealed record HourlyRateBand(DayOfWeek? Day, DateOnly? Date, int StartMinute, int EndMinute,
    [property: BsonRepresentation(BsonType.Decimal128)] decimal Rate);

public sealed record RentalRateOverride(DayOfWeek? Day, DateOnly? Date,
    [property: BsonRepresentation(BsonType.Decimal128)] decimal HalfDayRate,
    [property: BsonRepresentation(BsonType.Decimal128)] decimal WholeDayRate);
