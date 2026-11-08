using System.Text.RegularExpressions;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;

namespace HoleInHub.Data.Reservations;

public sealed record CustomerContactRecord(ReservationRecord Contact, string Kind);
public sealed record CustomerDirectoryRecords(IReadOnlyList<CustomerContactRecord> Items, bool HasMore);
public sealed record CustomerProfileRecords(CustomerContactRecord Customer, IReadOnlyList<ReservationRecord> Bookings,
    long Total, long Upcoming, long Completed, long Cancelled, bool HasMore);

public sealed partial class ReservationPersistence
{
    private static readonly TimeSpan CustomerQueryTimeout = TimeSpan.FromSeconds(5);
    private static BsonDocument ContactProjection() => new()
    {
        { "_id", 1 }, { "FirstName", 1 }, { "LastName", 1 }, { "Phone", 1 }, { "Email", 1 },
        { "Reference", 1 }, { "Status", 1 }, { "Service", 1 }, { "StartUtc", 1 }, { "EndUtc", 1 },
        { "CreatedUtc", 1 }, { "ResourceIds", 1 }, { "ResourceNames", 1 }, { "Attendees", 1 }
    };

    private static BsonDocument CustomerIdentity()
    {
        BsonDocument Value(string field) => new("$ifNull", new BsonArray { "$" + field, "" });
        BsonDocument NonEmpty(string field) => new("$ne", new BsonArray { Value(field), "" });
        BsonDocument Prefix(string field, string pattern) => new("$regexMatch", new BsonDocument { { "input", Value(field) }, { "regex", pattern } });
        BsonDocument Scope(string kind, BsonValue key) => new() { { "Kind", kind }, { "Key", key } };
        BsonDocument Account(string field) => new("$and", new BsonArray { NonEmpty(field), new BsonDocument("$not", new BsonArray { Prefix(field, "^(guest:|staff:)") }) });
        return new("$switch", new BsonDocument
        {
            { "branches", new BsonArray
                {
                    new BsonDocument { { "case", Account("CustomerAccountId") }, { "then", Scope("Account", "$CustomerAccountId") } },
                    new BsonDocument { { "case", Account("AccountId") }, { "then", Scope("Account", "$AccountId") } },
                    new BsonDocument
                    {
                        { "case", new BsonDocument("$and", new BsonArray { Prefix("AccountId", "^guest:"), NonEmpty("GuestSessionHash"), NonEmpty("Email") }) },
                        { "then", Scope("Guest", new BsonDocument("$toLower", Value("Email"))) }
                    }
                }
            },
            { "default", Scope("BookingContact", "$_id") }
        });
    }

    public async Task<CustomerDirectoryRecords> SearchCustomersAsync(string term, string[] phones, int page, CancellationToken ct)
    {
        var filter = Builders<ReservationRecord>.Filter;
        var search = FilterDefinition<ReservationRecord>.Empty;
        if (term.Length > 0)
        {
            var literal = new BsonRegularExpression(Regex.Escape(term), "i");
            var name = new BsonDocument("$expr", new BsonDocument("$regexMatch", new BsonDocument
            {
                { "input", new BsonDocument("$concat", new BsonArray { new BsonDocument("$ifNull", new BsonArray { "$FirstName", "" }), " ", new BsonDocument("$ifNull", new BsonArray { "$LastName", "" }) }) },
                { "regex", Regex.Escape(term) }, { "options", "i" }
            }));
            search = filter.Regex(x => x.Email, literal) | filter.Eq(x => x.Reference, term.ToUpperInvariant()) | name;
            if (phones.Length > 0) { search |= filter.In(x => x.Phone, phones); }
        }
        // shortcut: contact grouping scans matching reservations; add indexed search fields when measured volume exceeds the query budget.
        var rows = await Reservations.Aggregate(new AggregateOptions { MaxTime = CustomerQueryTimeout })
            .Match(search).SortByDescending(x => x.CreatedUtc).ThenByDescending(x => x.Id)
            .Project<ReservationRecord>(ContactProjectionWithIdentity())
            .Group<BsonDocument>(new BsonDocument { { "_id", CustomerIdentity() }, { "Contact", new BsonDocument("$first", "$$ROOT") } })
            .Sort(new BsonDocument { { "Contact.CreatedUtc", -1 }, { "Contact._id", -1 } })
            .Skip((page - 1) * 25).Limit(26)
            .Project<BsonDocument>(new BsonDocument { { "Kind", "$_id.Kind" }, { "Contact", ContactFields("$Contact") } })
            .ToListAsync(ct);
        return new(rows.Take(25).Select(row => new CustomerContactRecord(
            BsonSerializer.Deserialize<ReservationRecord>(row["Contact"].AsBsonDocument), row["Kind"].AsString)).ToArray(), rows.Count > 25);
    }

    private static BsonDocument ContactFields(string prefix)
    {
        var fields = new BsonDocument();
        foreach (var name in ContactProjection().Names) { fields[name] = prefix + "." + name; }
        return fields;
    }
    private static BsonDocument ContactProjectionWithIdentity() => ContactProjection()
        .Add("AccountId", 1).Add("CustomerAccountId", 1).Add("GuestSessionHash", 1);

    public async Task<CustomerProfileRecords?> ReadCustomerProfileAsync(string bookingId, bool upcomingOnly, int page, DateTime now, CancellationToken ct)
    {
        var identity = await Reservations.Aggregate(new AggregateOptions { MaxTime = CustomerQueryTimeout })
            .Match(x => x.Id == bookingId).Project<BsonDocument>(new BsonDocument("Identity", CustomerIdentity())).FirstOrDefaultAsync(ct);
        if (identity is null) { return null; }
        var scope = identity["Identity"].AsBsonDocument;
        var filter = new BsonDocument("$expr", new BsonDocument("$eq", new BsonArray { CustomerIdentity(), new BsonDocument("$literal", scope) }));
        var upcoming = new BsonDocument("$and", new BsonArray
        {
            new BsonDocument("$gt", new BsonArray { "$EndUtc", now }),
            new BsonDocument("$in", new BsonArray { "$Status", new BsonArray { "Pending", "Confirmed", "Checked-In" } })
        });
        BsonDocument Count(BsonValue condition) => new("$sum", new BsonDocument("$cond", new BsonArray { condition, 1, 0 }));
        var summary = await Reservations.Aggregate(new AggregateOptions { MaxTime = CustomerQueryTimeout }).Match(filter)
            .Group<BsonDocument>(new BsonDocument
            {
                { "_id", BsonNull.Value }, { "Total", new BsonDocument("$sum", 1) }, { "Upcoming", Count(upcoming) },
                { "Completed", Count(new BsonDocument("$eq", new BsonArray { "$Status", "Completed" })) },
                { "Cancelled", Count(new BsonDocument("$eq", new BsonArray { "$Status", "Cancelled" })) }
            }).FirstOrDefaultAsync(ct);
        if (summary is null) { return null; }
        var latest = await Reservations.Find(filter, new FindOptions { MaxTime = CustomerQueryTimeout })
            .SortByDescending(x => x.CreatedUtc).ThenByDescending(x => x.Id).Project<ReservationRecord>(ContactProjection()).FirstOrDefaultAsync(ct);
        if (latest is null) { return null; }
        var bookingFilter = upcomingOnly ? (FilterDefinition<ReservationRecord>)filter & new BsonDocument("$expr", upcoming) : (FilterDefinition<ReservationRecord>)filter;
        var bookings = await Reservations.Find(bookingFilter, new FindOptions { MaxTime = CustomerQueryTimeout })
            .Sort(upcomingOnly ? Builders<ReservationRecord>.Sort.Ascending(x => x.StartUtc).Ascending(x => x.Id)
                : Builders<ReservationRecord>.Sort.Descending(x => x.StartUtc).Descending(x => x.Id))
            .Skip((page - 1) * 25).Limit(26).Project<ReservationRecord>(ContactProjection()).ToListAsync(ct);
        return new(new(latest, scope["Kind"].AsString), bookings.Take(25).ToArray(), summary["Total"].ToInt64(),
            summary["Upcoming"].ToInt64(), summary["Completed"].ToInt64(), summary["Cancelled"].ToInt64(), bookings.Count > 25);
    }
}
