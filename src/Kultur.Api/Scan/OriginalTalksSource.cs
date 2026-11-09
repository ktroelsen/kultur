using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Kultur.Api.Events;
using Microsoft.Extensions.Options;

namespace Kultur.Api.Scan;

public class OriginalTalksOptions
{
    public bool Enabled { get; set; } = true;

    /// <summary>Pause between page requests (they asked for nothing; ~1 a second is polite).</summary>
    public int DelayMs { get; set; } = 1000;
}

// Original Talks (www.originaltalks.dk): talks with well-known people, sold mostly in their own shop.
// There's no API or feed. sitemap.xml lists every upcoming date; each show's page (one per talk, with
// all its dates) carries the full event objects as JSON inside Next.js' streamed payload
// (self.__next_f.push([1,"…"])). We keep only facts – title, speaker, time, venue, price – and link
// back; no descriptions or pictures (their terms reserve the content).
public partial class OriginalTalksSource(HttpClient http, IOptions<OriginalTalksOptions> options, ILogger<OriginalTalksSource> log) : IEventSource
{
    public const string Site = "https://www.originaltalks.dk";
    public const string SitemapUrl = Site + "/sitemap.xml";

    readonly OriginalTalksOptions _options = options.Value;

    public string Name => "originaltalks";
    public bool Configured => _options.Enabled;

    public async Task<List<CultureEvent>> FetchAsync(DateTimeOffset now, CancellationToken ct)
    {
        var shows = ShowPaths(await GetAsync(SitemapUrl, ct));
        var byId = new Dictionary<string, CultureEvent>();
        var failed = 0;
        foreach (var path in shows)
        {
            if (_options.DelayMs > 0) await Task.Delay(_options.DelayMs, ct);
            try
            {
                foreach (var e in Parse(await GetAsync(Site + path, ct), now)) byId[e.Id] = e;
            }
            catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or JsonException)
            {
                // One show that won't load mustn't cost us the rest.
                failed++;
                log.LogWarning(ex, "Original Talks show page {Path} failed", path);
            }
        }
        if (shows.Count > 0 && failed == shows.Count)
            throw new HttpRequestException("Original Talks: ingen af siderne kunne hentes.");
        return byId.Values.ToList();
    }

    async Task<string> GetAsync(string url, CancellationToken ct)
    {
        using var res = await http.GetAsync(url, ct);
        if (!res.IsSuccessStatusCode)
            throw new HttpRequestException($"Original Talks svarede {(int)res.StatusCode}.");
        return await res.Content.ReadAsStringAsync(ct);
    }

    /// <summary>
    /// The show pages (/events/{slug}-{id}) behind the sitemap's entries – both the shows themselves
    /// and their dated occurrences (/events/{slug}-{id}/{eventId}-{city}-{date}) – each once.
    /// Only paths on originaltalks.dk under /events/ are ever fetched.
    /// </summary>
    public static List<string> ShowPaths(string sitemapXml)
    {
        XDocument doc;
        try { doc = XDocument.Parse(sitemapXml); }
        catch (System.Xml.XmlException) { throw new HttpRequestException("Original Talks' sitemap kunne ikke læses."); }

        return doc.Descendants().Where(e => e.Name.LocalName == "loc")
            .Select(loc => Uri.TryCreate(loc.Value.Trim(), UriKind.Absolute, out var u) ? u : null)
            .Where(u => u is not null && u.Scheme == Uri.UriSchemeHttps && u.Host is "originaltalks.dk" or "www.originaltalks.dk")
            .Select(u => ShowPath().Match(u!.AbsolutePath))
            .Where(m => m.Success)
            .Select(m => m.Groups[1].Value)
            .Distinct()
            .ToList();
    }

    [GeneratedRegex(@"^(/events/[a-z0-9-]+-\d+)(/[a-z0-9-]+)?/?$")]
    private static partial Regex ShowPath();

    [GeneratedRegex(@"self\.__next_f\.push\(\[1,(""(?:[^""\\]|\\.)*"")\]\)")]
    private static partial Regex NextChunk();

    /// <summary>The events on a show page: every object in its payload that looks like an occurrence.</summary>
    public static List<CultureEvent> Parse(string html, DateTimeOffset now)
    {
        var payload = new StringBuilder();
        foreach (Match m in NextChunk().Matches(html))
        {
            try { payload.Append(JsonSerializer.Deserialize<string>(m.Groups[1].Value)); }
            catch (JsonException) { /* not a plain string chunk */ }
        }
        var text = payload.ToString();

        // Every object is a candidate: Tikko writes keys alphabetically ({"addons_left":…,"id":…}), and the
        // payload holds plenty of other objects, which ToEvent turns down.
        var events = new Dictionary<string, CultureEvent>();
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, Copenhagen).DateTime);
        for (var i = text.IndexOf('{'); i >= 0; i = text.IndexOf('{', i + 1))
        {
            var end = ObjectEnd(text, i);
            if (end < 0) continue;
            try
            {
                using var doc = JsonDocument.Parse(text.AsMemory(i, end - i + 1));
                if (ToEvent(doc.RootElement, now) is { } e && (e.Date is null || e.Date >= today)) events[e.Id] = e;
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException) { /* not an event after all */ }
        }
        return events.Values.ToList();
    }

    // Index of the brace closing the object that starts at `start`, minding strings; -1 if none.
    static int ObjectEnd(string s, int start)
    {
        var depth = 0;
        var inString = false;
        for (var i = start; i < s.Length; i++)
        {
            var c = s[i];
            if (inString)
            {
                if (c == '\\') i++;
                else if (c == '"') inString = false;
            }
            else if (c == '"') inString = true;
            else if (c == '{') depth++;
            else if (c == '}' && --depth == 0) return i;
        }
        return -1;
    }

    static CultureEvent? ToEvent(JsonElement o, DateTimeOffset now)
    {
        if (o.ValueKind != JsonValueKind.Object) return null;
        // Other objects in the payload also have an "id", often a string ({"id":"event-list"}).
        if (!o.TryGetProperty("id", out var idEl) || idEl.ValueKind != JsonValueKind.Number || !idEl.TryGetInt64(out var id)) return null;
        if (!o.TryGetProperty("unix_time_from", out var fromEl) || fromEl.ValueKind != JsonValueKind.Number || !fromEl.TryGetInt64(out var from)) return null;
        if (!o.TryGetProperty("venue", out var venue) || venue.ValueKind != JsonValueKind.Object) return null;
        var title = Str(o, "title");
        if (title is null) return null;
        if (Bool(o, "is_hidden") || Bool(o, "is_online")) return null;

        // Dates sold through Ticketmaster come from Ticketmaster already.
        var external = Str(o, "external_ticket_link");
        if (external is not null && external.Contains("ticketmaster.", StringComparison.OrdinalIgnoreCase)) return null;

        var start = DateTimeOffset.FromUnixTimeSeconds(from);
        var groupSlug = o.TryGetProperty("event_group_info", out var group) ? Str(group, "url_slug") : null;
        var slug = Str(o, "url_slug");
        var url = groupSlug is null ? Site + "/events"
            : slug is null ? $"{Site}/events/{groupSlug}"
            : $"{Site}/events/{groupSlug}/{id}-{slug}";
        if (!string.IsNullOrEmpty(external) && Uri.TryCreate(external, UriKind.Absolute, out var ext) && ext.Scheme == Uri.UriSchemeHttps)
            url = ext.ToString();

        var category = o.TryGetProperty("category", out var c) ? Str(c, "name")?.Trim() : null;
        var price = o.TryGetProperty("lowest_price_cents", out var p) && p.ValueKind == JsonValueKind.Number && p.TryGetInt64(out var cents) && cents > 0 ? cents / 100m : (decimal?)null;

        return new CultureEvent(
            Id: $"originaltalks:{id}",
            Source: "originaltalks",
            Title: title.Trim(),
            Category: Category.Talk,
            Genre: category,
            Date: DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(start, Copenhagen).DateTime),
            Start: start,
            Venue: Str(venue, "name"),
            City: Str(venue, "city") ?? Str(venue, "area_city"),
            CountryCode: Str(venue, "country")?.ToUpperInvariant() ?? "DK",
            PriceFrom: price,
            Currency: price is null ? null : Str(o, "currency") ?? "DKK",
            Url: url,
            SaleStart: null,
            ImageUrl: null,
            FirstSeenUtc: now,
            Performers: Speakers(Str(o, "speaker")));
    }

    /// <summary>"Glenn Bech og Dorte Bech Møldrup" → both names.</summary>
    public static List<string>? Speakers(string? speaker)
    {
        if (string.IsNullOrWhiteSpace(speaker)) return null;
        var names = Regex.Split(speaker, @"\s*,\s*|\s+og\s+|\s+&\s+")
            .Select(n => n.Trim())
            .Where(n => n.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return names.Count > 0 ? names : null;
    }

    static readonly TimeZoneInfo Copenhagen = FindCopenhagen();

    static TimeZoneInfo FindCopenhagen()
    {
        foreach (var id in new[] { "Europe/Copenhagen", "Romance Standard Time" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        }
        return TimeZoneInfo.Utc;
    }

    static string? Str(JsonElement e, string key) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    static bool Bool(JsonElement e, string key) =>
        e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.True;
}
