using System.Globalization;
using System.Text.Json;
using Kultur.Api.Events;
using Microsoft.Extensions.Options;

namespace Kultur.Api.Scan;

public class TicketmasterOptions
{
    public string? ApiKey { get; set; }

    /// <summary>Countries to fetch (ISO codes). Denmark for now; zone 3 countries can be added.</summary>
    public string[] Countries { get; set; } = ["DK"];

    /// <summary>Pages of 200 per country. The Discovery API never goes past 1000 results (5 pages).</summary>
    public int Pages { get; set; } = 5;

    public bool Configured => !string.IsNullOrWhiteSpace(ApiKey);
}

// Ticketmaster Discovery API (developer.ticketmaster.com): concerts, theatre, comedy and sport.
public class TicketmasterSource(HttpClient http, IOptions<TicketmasterOptions> options, ILogger<TicketmasterSource> log) : IEventSource
{
    public const string Endpoint = "https://app.ticketmaster.com/discovery/v2/events.json";
    const int PageSize = 200;

    readonly TicketmasterOptions _options = options.Value;

    public string Name => "ticketmaster";
    public bool Configured => _options.Configured;

    public async Task<List<CultureEvent>> FetchAsync(DateTimeOffset now, CancellationToken ct)
    {
        var result = new List<CultureEvent>();
        var pages = Math.Clamp(_options.Pages, 1, 1000 / PageSize);
        foreach (var country in _options.Countries)
        {
            for (var page = 0; page < pages; page++)
            {
                // The Discovery API takes its key as a query parameter.
                var url = $"{Endpoint}?apikey={Uri.EscapeDataString(_options.ApiKey!)}&countryCode={Uri.EscapeDataString(country)}" +
                          $"&size={PageSize}&page={page}&sort=date,asc&locale=*";
                using var res = await http.GetAsync(url, ct);
                if (!res.IsSuccessStatusCode)
                {
                    // Don't log the URL: it carries the key.
                    log.LogWarning("Ticketmaster answered {Status} for {Country} page {Page}", (int)res.StatusCode, country, page);
                    throw new HttpRequestException($"Ticketmaster svarede {(int)res.StatusCode}.");
                }
                var (events, totalPages) = Parse(await res.Content.ReadAsStringAsync(ct), now);
                result.AddRange(events);
                if (page + 1 >= totalPages) break;
                await Task.Delay(250, ct); // the API allows 5 requests a second
            }
        }
        return result;
    }

    public static (List<CultureEvent> Events, int TotalPages) Parse(string json, DateTimeOffset now)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var totalPages = root.TryGetProperty("page", out var p) && p.TryGetProperty("totalPages", out var tp) && tp.TryGetInt32(out var n) ? n : 1;
        var list = new List<CultureEvent>();
        if (!root.TryGetProperty("_embedded", out var embedded) || !embedded.TryGetProperty("events", out var events))
            return (list, totalPages);

        foreach (var e in events.EnumerateArray())
        {
            var id = Str(e, "id");
            var name = Str(e, "name");
            var url = Str(e, "url");
            if (id is null || name is null || url is null) continue;

            JsonElement? venue = e.TryGetProperty("_embedded", out var ee) && ee.TryGetProperty("venues", out var vs) && vs.GetArrayLength() > 0
                ? vs[0] : null;
            JsonElement? cls = e.TryGetProperty("classifications", out var cs) && cs.GetArrayLength() > 0 ? cs[0] : null;
            var segment = cls is { } c1 ? Str(c1, "segment", "name") : null;
            var genre = cls is { } c2 ? Str(c2, "genre", "name") : null;
            if (genre is "Undefined" or "Other") genre = null;

            JsonElement? price = e.TryGetProperty("priceRanges", out var pr) && pr.GetArrayLength() > 0 ? pr[0] : null;

            list.Add(new CultureEvent(
                Id: $"ticketmaster:{id}",
                Source: "ticketmaster",
                Title: name.Trim(),
                Category: CategoryOf(segment, genre),
                Genre: genre,
                Date: DateOnly.TryParseExact(Str(e, "dates", "start", "localDate"), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null,
                Start: DateTimeOffset.TryParse(Str(e, "dates", "start", "dateTime"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var s) ? s : null,
                Venue: venue is { } v1 ? Str(v1, "name") : null,
                City: venue is { } v2 ? Str(v2, "city", "name") : null,
                CountryCode: venue is { } v3 ? Str(v3, "country", "countryCode") : null,
                PriceFrom: price is { } p1 && p1.TryGetProperty("min", out var min) && min.TryGetDecimal(out var m) ? m : null,
                Currency: price is { } p2 ? Str(p2, "currency") : null,
                Url: url,
                SaleStart: DateTimeOffset.TryParse(Str(e, "sales", "public", "startDateTime"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var ss) ? ss : null,
                ImageUrl: BestImage(e),
                FirstSeenUtc: now));
        }
        return (list, totalPages);
    }

    static Category CategoryOf(string? segment, string? genre) => segment switch
    {
        "Music" => Category.Music,
        "Sports" => Category.Sport,
        "Film" => Category.Film,
        "Arts & Theatre" when genre is "Comedy" => Category.Comedy,
        "Arts & Theatre" => Category.Theatre,
        _ => Category.Other,
    };

    // A 16:9 picture around card size.
    static string? BestImage(JsonElement e)
    {
        if (!e.TryGetProperty("images", out var images)) return null;
        return images.EnumerateArray()
            .Where(i => Str(i, "ratio") == "16_9" && i.TryGetProperty("width", out var w) && w.TryGetInt32(out _))
            .OrderBy(i => Math.Abs(i.GetProperty("width").GetInt32() - 640))
            .Select(i => Str(i, "url"))
            .FirstOrDefault();
    }

    static string? Str(JsonElement e, params string[] path)
    {
        foreach (var key in path)
        {
            if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(key, out e)) return null;
        }
        return e.ValueKind == JsonValueKind.String ? e.GetString() : null;
    }
}
