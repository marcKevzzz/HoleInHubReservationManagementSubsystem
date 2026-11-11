using System.Text.RegularExpressions;
using HoleInHub.Data.Context;
using MongoDB.Bson;
using MongoDB.Driver;

namespace HoleInHub.Data.Activity;

public sealed class ActivityPersistence(IMongoDbContext context)
{
    public async Task<IReadOnlyList<ActivityRecord>> QueryAsync(ActivityQuery query, bool includeAccess, CancellationToken ct)
    {
        var rows = new List<ActivityRecord>();
        foreach (var source in Sources(includeAccess).Where(x => query.Category is null || x.Category == query.Category))
        {
            var pipeline = Pipeline(source, query);
            rows.AddRange(await ReadAsync(source.Collection, pipeline, ct));
        }
        return rows.OrderByDescending(x => x.OccurredUtc).ThenByDescending(x => x.Key, StringComparer.Ordinal).Take(51).ToArray();
    }

    public async Task<ActivityRecord?> GetAsync(string identity, bool includeAccess, CancellationToken ct)
    {
        var parts = identity.Split('|');
        if (parts.Length != 3 || parts[1].Length == 0 || parts[1].Length > 200) { return null; }
        var source = Sources(includeAccess).FirstOrDefault(x => x.Code == parts[0]);
        if (source is null) { return null; }
        var stages = Initial(source, new BsonDocument("_id", parts[1]));
        stages.Add(new BsonDocument("$match", new BsonDocument { { "Key", identity }, { "OccurredUtc", new BsonDocument("$type", "date") } }));
        stages.Add(new BsonDocument("$limit", 1));
        AddActorLabel(stages);
        return (await ReadAsync(source.Collection, stages, ct)).FirstOrDefault();
    }

    private Task<List<ActivityRecord>> ReadAsync(string collection, List<BsonDocument> stages, CancellationToken ct) =>
        context.Database.GetCollection<BsonDocument>(collection).Aggregate<ActivityRecord>(stages,
            new AggregateOptions { MaxTime = TimeSpan.FromSeconds(15) }).ToListAsync(ct);

    private sealed record Source(string Code, string Collection, string Category, string Time, string Actor);
    private static IEnumerable<Source> Sources(bool access)
    {
        yield return new("r", "reservations", "Reservations", "History.AtUtc", "History.Actor");
        yield return new("b", "schedule_blocks", "Schedule", "CreatedUtc", "CreatedBy");
        yield return new("u", "schedule_blocks", "Schedule", "ReleasedUtc", "ReleasedBy");
        yield return new("c", "customer_profile_operations", "Customers", "OccurredUtc", "ActorId");
        if (access) { yield return new("a", "staff_role_audit", "Access", "OccurredUtc", "ActorAccountId"); }
    }

    private static BsonDocument Range(DateTime from, DateTime to, DateTime asOf) =>
        new() { { "$gte", from }, { "$lt", to }, { "$lte", asOf } };
    private static List<BsonDocument> Pipeline(Source source, ActivityQuery query)
    {
        // Match parents before unwind, then match exact events; histories are append-only in current writers.
        var match = source.Code == "r"
            ? new BsonDocument("History", new BsonDocument("$elemMatch", new BsonDocument("AtUtc", Range(query.FromUtc, query.ToUtc, query.AsOfUtc))))
            : new BsonDocument(source.Time, Range(query.FromUtc, query.ToUtc, query.AsOfUtc));
        var stages = Initial(source, match);
        var exact = new BsonDocument("OccurredUtc", Range(query.FromUtc, query.ToUtc, query.AsOfUtc));
        if (query.ActorId is not null) { exact.Add("Actor", query.ActorId); }
        if (query.After is not null)
        {
            exact.Add("$or", new BsonArray {
                new BsonDocument("OccurredUtc", new BsonDocument("$lt", query.After.OccurredUtc)),
                new BsonDocument { { "OccurredUtc", query.After.OccurredUtc }, { "Key", new BsonDocument("$lt", query.After.Key) } }
            });
        }
        stages.Add(new BsonDocument("$match", exact));
        if (!string.IsNullOrEmpty(query.Search))
        {
            AddActorLabel(stages);
            var regex = new BsonRegularExpression(Regex.Escape(query.Search), "i");
            stages.Add(new BsonDocument("$match", new BsonDocument("$or", new BsonArray {
                new BsonDocument("TargetLabel", regex), new BsonDocument("ActorLabel", regex)
            })));
        }
        stages.Add(new BsonDocument("$sort", new BsonDocument { { "OccurredUtc", -1 }, { "Key", -1 } }));
        stages.Add(new BsonDocument("$limit", 51));
        if (string.IsNullOrEmpty(query.Search)) { AddActorLabel(stages); }
        return stages;
    }

    private static BsonValue Value(string field) => new BsonDocument("$ifNull", new BsonArray { "$" + field, "" });
    private static BsonValue Name(string prefix) => new BsonDocument("$trim", new BsonDocument("input",
        new BsonDocument("$concat", new BsonArray { Value(prefix + "FirstName"), " ", Value(prefix + "LastName") })));
    private static List<BsonDocument> Initial(Source source, BsonDocument match)
    {
        var stages = new List<BsonDocument> { new("$match", match) };
        if (source.Code == "r")
        {
            stages.Add(new BsonDocument("$unwind", new BsonDocument { { "path", "$History" }, { "includeArrayIndex", "Ordinal" } }));
        }
        var projection = new BsonDocument
        {
            { "_id", 0 }, { "Key", new BsonDocument("$concat", new BsonArray { source.Code + "|", "$_id", "|",
                source.Code == "r" ? new BsonDocument("$toString", "$Ordinal") : new BsonString("event") }) },
            { "OccurredUtc", "$" + source.Time }, { "Actor", Value(source.Actor) },
            { "ActorLabel", source.Code == "c" ? Value("ActorLabel") : new BsonString("") },
            { "Category", source.Category }, { "TargetId", "$_id" }
        };
        switch (source.Code)
        {
            case "r":
                projection.Add("TargetLabel", Value("Reference")).Add("TargetKind", "reservation")
                    .Add("EventType", Value("History.EventType")).Add("Reason", Value("History.Reason"))
                    .Add("PreviousStatus", "$History.PreviousReservationStatus").Add("Status", Value("History.Status"))
                    .Add("TargetStartUtc", "$History.ScheduleChange.StartUtc")
                    .Add("BeforeStartUtc", "$History.ScheduleChange.OriginalStartUtc").Add("AfterStartUtc", "$History.ScheduleChange.StartUtc")
                    .Add("BeforeEndUtc", "$History.ScheduleChange.OriginalEndUtc").Add("AfterEndUtc", "$History.ScheduleChange.EndUtc")
                    .Add("BeforeResources", "$History.ScheduleChange.OriginalResourceIds").Add("AfterResources", "$History.ScheduleChange.ResourceIds");
                break;
            case "b":
            case "u":
                projection.Add("TargetLabel", new BsonDocument("$reduce", new BsonDocument {
                    { "input", new BsonDocument("$ifNull", new BsonArray { "$ResourceIds", new BsonArray() }) },
                    { "initialValue", "Blocked time" }, { "in", new BsonDocument("$concat", new BsonArray { "$$value", " / ", "$$this" }) }
                })).Add("TargetKind", "schedule")
                    .Add("EventType", source.Code == "b" ? "BlockCreated" : "BlockReleased")
                    .Add("Reason", Value(source.Code == "b" ? "Reason" : "ReleaseReason"))
                    .Add("TargetStartUtc", "$StartUtc").Add("AfterStartUtc", "$StartUtc").Add("AfterEndUtc", "$EndUtc")
                    .Add("AfterResources", "$ResourceIds");
                break;
            case "c":
                projection["TargetId"] = "$TargetId";
                projection.Add("TargetKind", "customer-merge").Add("EventType", Value("Kind")).Add("Reason", Value("Reason"))
                    .Add("SourceName", Name("SourceBefore.")).Add("TargetName", Name("TargetBefore."))
                    .Add("TargetLabel", new BsonDocument("$concat", new BsonArray { Name("SourceBefore."), " / ", Name("TargetBefore.") }))
                    .Add("ReservationCount", new BsonDocument("$size", new BsonDocument("$ifNull", new BsonArray { "$ReservationIds", new BsonArray() })));
                break;
            case "a":
                projection["TargetId"] = "$TargetAccountId";
                projection.Add("TargetLabel", "Team member")
                    .Add("TargetKind", "account").Add("EventType", Value("EventType")).Add("Reason", Value("Reason"))
                    .Add("PreviousRoles", "$PreviousRoles").Add("NewRoles", "$NewRoles");
                break;
        }
        stages.Add(new BsonDocument("$project", projection));
        if (source.Code is "b" or "u") { stages.Add(new BsonDocument("$match", new BsonDocument("Actor", new BsonDocument("$ne", "")))); }
        return stages;
    }

    private static void AddActorLabel(List<BsonDocument> stages)
    {
        // A lookup projects names only, never credentials/contact details or today's roles.
        stages.Add(new BsonDocument("$lookup", new BsonDocument
        {
            { "from", "accounts" }, { "let", new BsonDocument("actor", "$Actor") },
            { "pipeline", new BsonArray {
                new BsonDocument("$match", new BsonDocument("$expr", new BsonDocument("$eq", new BsonArray { "$_id", "$$actor" }))),
                new BsonDocument("$project", new BsonDocument { { "_id", 0 }, { "Name", Name("") } })
            } }, { "as", "ActorAccount" }
        }));
        stages.Add(new BsonDocument("$set", new BsonDocument("ActorLabel",
            new BsonDocument("$cond", new BsonArray {
                new BsonDocument("$ne", new BsonArray { "$ActorLabel", "" }), "$ActorLabel",
                new BsonDocument("$ifNull", new BsonArray { new BsonDocument("$arrayElemAt", new BsonArray { "$ActorAccount.Name", 0 }), "" })
            }))));
        stages.Add(new BsonDocument("$unset", "ActorAccount"));
    }
}
