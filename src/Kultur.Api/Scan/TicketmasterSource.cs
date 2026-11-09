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

    /// <summary>How far ahead to look, in calendar months (the current one counts as the first).</summary>
    public int Months { get; set; } = 12;

    /// <summary>Pause between calls; the API allows 5 requests a second.</summary>
    public int DelayMs { get; set; } = 250;

    public bool Configured => !string.IsNullOrWhiteSpace(ApiKey);
}

// Ticketmaster Discovery API (developer.ticketmaster.com): concerts, theatre, comedy and sport.
public class TicketmasterSource(HttpClient http, IOptions<TicketmasterOptions> options, ILogger<TicketmasterSource> log) : IEventSource
{
    public const string Endpoint = "https://app.ticketmaster.com/discovery/v2/events.json";
    const int PageSize = 200;
    const int MaxResults = 1000;
    static readonly TimeSpan MinWindow = TimeSpan.FromDays(1);

    readonly TicketmasterOptions _options = options.Value;

    // A key pasted into a secret easily picks up a space or a line break.
    string Key => _options.ApiKey!.Trim();

    public string Name => "ticketmaster";
    public bool Configured => _options.Configured;

    // The Discovery API never returns more than 1000 results for one search (5 pages of 200), so
    // we search month by month, and split a month in halves while it still has more than that.
    public async Task<List<CultureEvent>> FetchAsync(DateTimeOffset now, CancellationToken ct)
    {
        var byId = new Dictionary<string, CultureEvent>();
        var from = DateTimeOffset.FromUnixTimeSeconds(now.ToUnixTimeSeconds());
        var firstOfMonth = new DateTimeOffset(from.Year, from.Month, 1, 0, 0, 0, TimeSpan.Zero);
        foreach (var country in _options.Countries)
        {
            var start = from;
            for (var month = 1; month <= Math.Max(1, _options.Months); month++)
            {
                var end = firstOfMonth.AddMonths(month);
                await FetchWindowAsync(country, start, end, now, byId, ct);
                start = end;
            }
        }
        return byId.Values.ToList();
    }

    async Task FetchWindowAsync(string country, DateTimeOffset start, DateTimeOffset end, DateTimeOffset now,
        Dictionary<string, CultureEvent> byId, CancellationToken ct)
    {
        for (var page = 0; page < MaxResults / PageSize; page++)
        {
            var (events, totalPages, totalElements) = await GetPageAsync(country, start, end, page, now, ct);
            if (page == 0 && totalElements > MaxResults)
            {
                if (end - start > MinWindow)
                {
                    var middle = DateTimeOffset.FromUnixTimeSeconds((start.ToUnixTimeSeconds() + end.ToUnixTimeSeconds()) / 2);
                    await FetchWindowAsync(country, start, middle, now, byId, ct);
                    await FetchWindowAsync(country, middle, end, now, byId, ct);
                    return;
                }
                log.LogWarning("Ticketmaster has {Count} events in {Country} from {Start} – only the first {Max} are fetched",
                    totalElements, country, start, MaxResults);
            }
            foreach (var e in events) byId[e.Id] = e;
            if (page + 1 >= totalPages) break;
        }
    }

    async Task<(List<CultureEvent> Events, int TotalPages, int TotalElements)> GetPageAsync(
        string country, DateTimeOffset start, DateTimeOffset end, int page, DateTimeOffset now, CancellationToken ct)
    {
        // The Discovery API takes its key as a query parameter.
        var url = $"{Endpoint}?apikey={Uri.EscapeDataString(Key)}&countryCode={Uri.EscapeDataString(country)}" +
                  $"&startDateTime={Iso(start)}&endDateTime={Iso(end)}" +
                  $"&size={PageSize}&page={page}&sort=date,asc&locale=*";
        using var res = await http.GetAsync(url, ct);
        if (!res.IsSuccessStatusCode)
        {
            // Don't log the URL: it carries the key.
            var reason = Reason(await res.Content.ReadAsStringAsync(ct))?.Replace(Key, "…");
            log.LogWarning("Ticketmaster answered {Status} ({Reason}) for {Country} from {Start} page {Page}",
                (int)res.StatusCode, reason, country, start, page);
            throw new HttpRequestException(reason is null
                ? $"Ticketmaster svarede {(int)res.StatusCode}."
                : $"Ticketmaster svarede {(int)res.StatusCode}: {reason}");
        }
        var result = Parse(await res.Content.ReadAsStringAsync(ct), now);
        if (_options.DelayMs > 0) await Task.Delay(_options.DelayMs, ct);
        return result;
    }

    static string Iso(DateTimeOffset t) => t.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    public static (List<CultureEvent> Events, int TotalPages, int TotalElements) Parse(string json, DateTimeOffset now)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var hasPage = root.TryGetProperty("page", out var p);
        var totalPages = hasPage && p.TryGetProperty("totalPages", out var tp) && tp.TryGetInt32(out var n) ? n : 1;
        var totalElements = hasPage && p.TryGetProperty("totalElements", out var te) && te.TryGetInt32(out var elements) ? elements : 0;
        var list = new List<CultureEvent>();
        if (!root.TryGetProperty("_embedded", out var embedded) || !embedded.TryGetProperty("events", out var events))
            return (list, totalPages, totalElements);

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
                FirstSeenUtc: now,
                Performers: Performers(e)));
        }
        return (list, totalPages, totalElements);
    }

    /// <summary>
    /// Ticketmaster's own explanation of an error: {"fault":{"faultstring":"Invalid ApiKey"}} from the
    /// gateway, or {"errors":[{"detail":"…"}]} from the API itself.
    /// </summary>
    public static string? Reason(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            var text = Str(root, "fault", "faultstring");
            if (text is null && root.ValueKind == JsonValueKind.Object && root.TryGetProperty("errors", out var errors) &&
                errors.ValueKind == JsonValueKind.Array && errors.GetArrayLength() > 0)
                text = Str(errors[0], "detail");
            return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        }
        catch (JsonException)
        {
            return null;
        }
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

    // Artists, bands or teams ("attractions"), in Ticketmaster's order.
    static List<string>? Performers(JsonElement e)
    {
        if (!e.TryGetProperty("_embedded", out var embedded) || !embedded.TryGetProperty("attractions", out var attractions) ||
            attractions.ValueKind != JsonValueKind.Array)
            return null;
        var names = attractions.EnumerateArray()
            .Select(a => Str(a, "name")?.Trim())
            .OfType<string>()
            .Where(n => n.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return names.Count > 0 ? names : null;
    }

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
