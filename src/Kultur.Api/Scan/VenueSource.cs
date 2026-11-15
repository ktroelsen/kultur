using System.Globalization;
using System.Text.Json;
using Anthropic;
using Anthropic.Models.Messages;
using Kultur.Api.Ai;
using Kultur.Api.Events;
using Microsoft.Extensions.Options;

namespace Kultur.Api.Scan;

/// <summary>
/// A place to keep an eye on: its programme pages and where it is. Notes: what to include or leave out
/// (e.g. the planetarium's daily dome films).
/// </summary>
public record Venue(string Key, string Name, string[] Urls, string City, string CountryCode, string? Notes = null);

public class VenuesOptions
{
    public List<Venue> Venues { get; set; } =
    [
        new("arena-verona", "Arena di Verona", ["https://www.arena.it/"], "Verona", "IT",
            "The opera festival in the Roman arena and other concerts there. Each opera title is its own series."),
        new("planetariet", "Planetariet", ["https://www.planetariet.dk/"], "København V", "DK",
            "Tycho Brahe Planetarium. Include talks, concerts, special evenings and premieres of new dome shows. " +
            "Leave out the regular daily dome-film screenings, except a new show's first day."),
    ];

    /// <summary>Programmes change slowly: read each venue again after this many days.</summary>
    public int IntervalDays { get; set; } = 7;

    /// <summary>Upper bound on pages fetched per venue and look-up (a cost cap).</summary>
    public int MaxFetchesPerVenue { get; set; } = 8;

    /// <summary>Upper bound on how much of each fetched page Claude reads, in tokens (a cost cap).</summary>
    public int MaxTokensPerPage { get; set; } = 30_000;
}

/// <summary>One event as Claude read it from a venue's pages.</summary>
public record VenueEvent(
    string? Title, string? Series, string? Date, string? Time, string? Hall, string? Category, string? Genre,
    string[]? Performers, decimal? PriceFrom, string? Currency, string? Url);

public record VenuesState(Dictionary<string, DateTimeOffset> LookedUpUtc);

// Venues without a feed or API. Once a week per venue, Claude fetches the venue's own pages (only that
// site) and answers with its upcoming programme as JSON – facts only: title, date, time, price, link.
// Each look-up replaces that venue's events, so cancelled or renamed ones disappear. Needs Ai:ApiKey.
public class VenueSource(AnthropicClient client, IOptions<AiOptions> ai, IOptions<VenuesOptions> options,
    StatePath statePath, ILogger<VenueSource> log) : IEventSource
{
    readonly VenuesOptions _options = options.Value;
    readonly List<string> _replaces = [];

    public string Name => "steder";
    public bool Configured => ai.Value.Configured && _options.Venues.Count > 0;
    public IReadOnlyList<string> Replaces => _replaces;

    public static string IdPrefix(Venue v) => $"venue:{v.Key}:";

    public async Task<List<CultureEvent>> FetchAsync(DateTimeOffset now, CancellationToken ct)
    {
        var state = await JsonFile.ReadAsync<VenuesState>(statePath.Venues, ct) ?? new VenuesState([]);
        var due = _options.Venues
            .Where(v => !state.LookedUpUtc.TryGetValue(v.Key, out var at) || now - at >= TimeSpan.FromDays(_options.IntervalDays))
            .ToList();

        var events = new System.Collections.Concurrent.ConcurrentBag<CultureEvent>();
        var done = new System.Collections.Concurrent.ConcurrentBag<Venue>();
        await Parallel.ForEachAsync(due, new ParallelOptions { MaxDegreeOfParallelism = 2, CancellationToken = ct }, async (venue, token) =>
        {
            try
            {
                foreach (var e in await LookUpAsync(venue, now, token)) events.Add(e);
                done.Add(venue);
            }
            catch (Exception ex) when (ex is HttpRequestException or Anthropic.Exceptions.AnthropicException)
            {
                log.LogWarning(ex, "Look-up of {Venue} failed", venue.Name);
            }
        });

        if (due.Count > 0 && done.IsEmpty) throw new HttpRequestException("Ingen af stederne kunne læses.");
        foreach (var v in done)
        {
            state.LookedUpUtc[v.Key] = now;
            _replaces.Add(IdPrefix(v));
        }
        await JsonFile.WriteAsync(statePath.Venues, state, ct);
        return events.DistinctBy(e => e.Id).ToList();
    }

    async Task<List<CultureEvent>> LookUpAsync(Venue venue, DateTimeOffset now, CancellationToken ct)
    {
        var domains = venue.Urls.Select(u => new Uri(u).Host).Distinct().ToList();
        var categories = string.Join(", ", Enum.GetNames<Events.Category>());
        var prompt = $"""
            Today is {now:yyyy-MM-dd}. Read the programme of {venue.Name} ({venue.City}, {venue.CountryCode}) on its own
            website, starting here: {string.Join(" ", venue.Urls)}
            Follow its links to the programme / calendar / event pages as needed. {venue.Notes}

            List every upcoming event (from today on) with its dates. One entry per date and time, with:
            - title: the event's name as the venue writes it
            - series: what ties the dates of the same production together (e.g. the opera's title), same text for each date
            - date: YYYY-MM-DD, and time: HH:mm local time, or null
            - hall: the hall or stage at the venue, or null
            - category: one of {categories}
            - genre: e.g. "Opera", "Ballet", "Klassisk", "Foredrag", "Planetarieshow", or null
            - performers: the main artists or speakers, as a list (may be empty)
            - priceFrom and currency: the lowest ticket price, or null
            - url: the event's own page on the site, or null

            Only facts from the site; don't guess. No descriptions.
            Answer with only a JSON object, no other text: {"{"}"events": [ ... ]{"}"}
            """;

        var response = await client.Messages.Create(new MessageCreateParams
        {
            Model = ai.Value.Model,
            MaxTokens = 32000,
            OutputConfig = new OutputConfig { Effort = Effort.Medium },
            Tools = [new ToolUnion(new WebFetchTool20260209 { AllowedDomains = domains, MaxUses = _options.MaxFetchesPerVenue, MaxContentTokens = _options.MaxTokensPerPage })],
            Messages = [new() { Role = Role.User, Content = prompt }],
        }, ct);

        if (response.StopReason == StopReason.Refusal)
            throw new HttpRequestException($"Claude ville ikke læse {venue.Name}.");

        var text = string.Concat(response.Content.Select(b => b.TryPickText(out var t) ? t.Text : ""));
        var events = ParseEvents(text).Select(e => ToEvent(venue, e, now)).OfType<CultureEvent>().ToList();
        log.LogInformation("{Venue}: {Count} events", venue.Name, events.Count);
        return events;
    }

    public static List<VenueEvent> ParseEvents(string text)
    {
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start < 0 || end <= start) throw new HttpRequestException("Claude svarede ikke med programmet.");
        try
        {
            using var doc = JsonDocument.Parse(text[start..(end + 1)]);
            if (!doc.RootElement.TryGetProperty("events", out var events) || events.ValueKind != JsonValueKind.Array)
                throw new HttpRequestException("Claude svarede ikke med programmet.");
            return events.Deserialize<List<VenueEvent>>(JsonFile.Options) ?? [];
        }
        catch (JsonException)
        {
            throw new HttpRequestException("Claude svarede ikke med et gyldigt program.");
        }
    }

    public static CultureEvent? ToEvent(Venue venue, VenueEvent e, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(e.Title)) return null;
        if (!DateOnly.TryParseExact(e.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) return null;
        if (date < DateOnly.FromDateTime(now.UtcDateTime)) return null;
        var time = TimeOnly.TryParseExact(e.Time, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var t) ? t : (TimeOnly?)null;

        // Only the venue's own pages are linked to; otherwise its front page.
        var hosts = venue.Urls.Select(u => new Uri(u).Host).ToHashSet();
        var url = Uri.TryCreate(e.Url, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps && hosts.Contains(u.Host)
            ? u.ToString() : venue.Urls[0];
        var title = e.Title.Trim();
        var series = string.IsNullOrWhiteSpace(e.Series) ? title : e.Series.Trim();

        return new CultureEvent(
            Id: $"{IdPrefix(venue)}{Slugs.Of(title)}-{date:yyyyMMdd}{(time is { } at ? $"-{at:HHmm}" : "")}",
            Source: venue.Key,
            Title: title,
            Category: Enum.TryParse<Events.Category>(e.Category, ignoreCase: true, out var c) ? c : Events.Category.Other,
            Genre: string.IsNullOrWhiteSpace(e.Genre) ? null : e.Genre.Trim(),
            Date: date,
            Start: time is { } local ? LocalTime(date, local) : null,
            Venue: string.IsNullOrWhiteSpace(e.Hall) ? venue.Name : $"{venue.Name} – {e.Hall.Trim()}",
            City: venue.City,
            CountryCode: venue.CountryCode,
            PriceFrom: e.PriceFrom is > 0 ? e.PriceFrom : null,
            Currency: e.PriceFrom is > 0 ? (e.Currency ?? (venue.CountryCode == "DK" ? "DKK" : "EUR")).Trim().ToUpperInvariant() : null,
            Url: url,
            SaleStart: null,
            ImageUrl: null,
            FirstSeenUtc: now,
            Performers: e.Performers?.Select(p => p.Trim()).Where(p => p.Length > 0).Distinct().ToList() is { Count: > 0 } p ? p : null,
            Series: $"venue:{venue.Key}:{Slugs.Of(series)}");
    }

    // Both venues so far are on Central European time.
    static DateTimeOffset LocalTime(DateOnly date, TimeOnly time)
    {
        var zone = CentralEurope.Value;
        var local = date.ToDateTime(time);
        return new DateTimeOffset(local, zone.GetUtcOffset(local));
    }

    static readonly Lazy<TimeZoneInfo> CentralEurope = new(() =>
    {
        foreach (var id in new[] { "Europe/Copenhagen", "Romance Standard Time" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (TimeZoneNotFoundException) { }
        }
        return TimeZoneInfo.Utc;
    });
}
