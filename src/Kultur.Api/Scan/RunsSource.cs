using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Anthropic;
using Anthropic.Models.Messages;
using Kultur.Api.Ai;
using Kultur.Api.Events;
using Microsoft.Extensions.Options;

namespace Kultur.Api.Scan;

/// <summary>A race to follow: its name and where it's run.</summary>
public record Race(string Name, string City, string CountryCode);

public class RunsOptions
{
    /// <summary>The races to follow. Each one becomes a performer you can rate under "Smag".</summary>
    public List<Race> Races { get; set; } =
    [
        new("Berlin Marathon", "Berlin", "DE"),
        new("London Marathon", "London", "GB"),
        new("New York City Marathon", "New York", "US"),
        new("Chicago Marathon", "Chicago", "US"),
        new("Boston Marathon", "Boston", "US"),
        new("Tokyo Marathon", "Tokyo", "JP"),
        new("Sydney Marathon", "Sydney", "AU"),
        new("Copenhagen Marathon", "København", "DK"),
        new("Copenhagen Half Marathon", "København", "DK"),
        new("Royal Run", "København", "DK"),
        new("DHL Stafetten", "København", "DK"),
        new("Eremitageløbet", "Klampenborg", "DK"),
    ];

    /// <summary>Web searches cost money and race dates rarely move: look again after this many days.</summary>
    public int IntervalDays { get; set; } = 7;

    /// <summary>Upper bound on web searches per race (a cost cap; a search costs about $0.01).</summary>
    public int MaxSearchesPerRace { get; set; } = 5;
}

/// <summary>What Claude found for one edition of a race.</summary>
public record RaceEdition(
    string Name, int? Year, string? Date, string? City, string? CountryCode, string? Distance,
    string? Url, string? RegistrationOpens, string? Registration);

/// <summary>The last look-up, kept so the weekly interval survives app-pool recycles.</summary>
public record RunsState(DateTimeOffset LookedUpUtc, List<CultureEvent> Events);

// Big races – marathons abroad, the classics at home. No feed lists them all, so once a week Claude
// searches the web for each race's next editions: date, official site and when registration (or the
// ballot) opens, which is what matters most for a race like Berlin. Needs Ai:ApiKey.
public partial class RunsSource(AnthropicClient client, IOptions<AiOptions> ai, IOptions<RunsOptions> options,
    StatePath statePath, ILogger<RunsSource> log) : IEventSource
{
    readonly RunsOptions _options = options.Value;

    public string Name => "løb";
    public bool Configured => ai.Value.Configured && _options.Races.Count > 0;

    public async Task<List<CultureEvent>> FetchAsync(DateTimeOffset now, CancellationToken ct)
    {
        // Within the interval nothing is fetched: the catalog keeps last week's races.
        var state = await JsonFile.ReadAsync<RunsState>(statePath.Runs, ct);
        if (state is not null && now - state.LookedUpUtc < TimeSpan.FromDays(_options.IntervalDays))
            return [];

        var editions = await LookUpAsync(now, ct);
        var events = editions.Select(e => ToEvent(e, now)).OfType<CultureEvent>()
            .DistinctBy(e => e.Id)
            .ToList();
        await JsonFile.WriteAsync(statePath.Runs, new RunsState(now, events), ct);
        return events;
    }

    // One call per race keeps each search loop short (no paused turns) and lets one race fail alone.
    // A look-up with web search takes up to a minute, so a few run at once.
    async Task<List<RaceEdition>> LookUpAsync(DateTimeOffset now, CancellationToken ct)
    {
        var found = new System.Collections.Concurrent.ConcurrentBag<RaceEdition>();
        var failed = 0;
        await Parallel.ForEachAsync(_options.Races, new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = ct },
            async (race, token) =>
            {
                try
                {
                    foreach (var e in await LookUpAsync(race, now, token)) found.Add(e);
                }
                catch (Exception ex) when (ex is HttpRequestException or Anthropic.Exceptions.AnthropicException)
                {
                    Interlocked.Increment(ref failed);
                    log.LogWarning(ex, "Look-up of {Race} failed", race.Name);
                }
            });
        if (failed == _options.Races.Count) throw new HttpRequestException("Ingen af løbene kunne slås op.");
        return found.ToList();
    }

    async Task<List<RaceEdition>> LookUpAsync(Race race, DateTimeOffset now, CancellationToken ct)
    {
        var prompt = $"""
            Today is {now:yyyy-MM-dd}. Find on the web the next editions of {race.Name} ({race.City}, {race.CountryCode})
            that haven't been run yet: the coming one, and the one after it if its date is already announced.

            For each edition, give:
            - name: "{race.Name}"
            - year
            - date: race day as YYYY-MM-DD (the main race if there are several days), or null if not announced
            - city and countryCode (ISO 3166-1 alpha-2)
            - distance: e.g. "Marathon", "Halvmaraton", "10 km", "Stafet"
            - url: the race's official website
            - registrationOpens: when registration or the ballot/lottery opens, as YYYY-MM-DD, or null if
              unknown. If it has already opened, give the date it opened.
            - registration: one short sentence in Danish on how to get a place (lodtrækning, først til mølle,
              udsolgt, velgørenhed …), and when it closes if known

            Prefer the official race website. Don't guess dates: use null when you can't find them.
            Answer with only a JSON object, no other text: {"{"}"editions": [ ... ]{"}"}
            """;

        var response = await client.Messages.Create(new MessageCreateParams
        {
            Model = ai.Value.Model,
            MaxTokens = 16000,
            OutputConfig = new OutputConfig { Effort = Effort.Medium },
            Tools = [new ToolUnion(new WebSearchTool20260209 { MaxUses = _options.MaxSearchesPerRace })],
            Messages = [new() { Role = Role.User, Content = prompt }],
        }, ct);

        if (response.StopReason == StopReason.Refusal)
            throw new HttpRequestException($"Claude ville ikke slå {race.Name} op.");

        var text = string.Concat(response.Content.Select(b => b.TryPickText(out var t) ? t.Text : ""));
        return ParseEditions(text);
    }

    /// <summary>The JSON object in Claude's answer (also when wrapped in a code fence or text).</summary>
    public static List<RaceEdition> ParseEditions(string text)
    {
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start < 0 || end <= start) throw new HttpRequestException("Claude svarede ikke med løbene.");
        try
        {
            using var doc = JsonDocument.Parse(text[start..(end + 1)]);
            if (!doc.RootElement.TryGetProperty("editions", out var editions) || editions.ValueKind != JsonValueKind.Array)
                throw new HttpRequestException("Claude svarede ikke med løbene.");
            return editions.Deserialize<List<RaceEdition>>(JsonFile.Options) ?? [];
        }
        catch (JsonException)
        {
            throw new HttpRequestException("Claude svarede ikke med gyldige løbsdata.");
        }
    }

    public CultureEvent? ToEvent(RaceEdition e, DateTimeOffset now)
    {
        // Only races on our list, under the list's own name.
        var race = _options.Races.FirstOrDefault(r => r.Name.Equals(e.Name?.Trim(), StringComparison.OrdinalIgnoreCase));
        if (race is null) return null;

        DateOnly? date = DateOnly.TryParseExact(e.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
        var year = date?.Year ?? e.Year;
        if (year is null) return null;
        if (date is { } day && day < DateOnly.FromDateTime(now.UtcDateTime)) return null;

        DateTimeOffset? opens = DateOnly.TryParseExact(e.RegistrationOpens, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var o)
            ? new DateTimeOffset(o.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero)
            : null;
        var url = Uri.TryCreate(e.Url, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps ? u.ToString()
            : $"https://www.google.com/search?q={Uri.EscapeDataString($"{race.Name} {year}")}";

        return new CultureEvent(
            Id: $"runs:{Slug(race.Name)}-{year}",
            Source: "løb",
            Title: $"{race.Name} {year}",
            Category: Events.Category.Running,
            Genre: string.IsNullOrWhiteSpace(e.Distance) ? null : e.Distance.Trim(),
            Date: date,
            Start: null,
            Venue: null,
            City: string.IsNullOrWhiteSpace(e.City) ? race.City : e.City.Trim(),
            CountryCode: (e.CountryCode ?? race.CountryCode).Trim().ToUpperInvariant(),
            PriceFrom: null,
            Currency: null,
            Url: url,
            SaleStart: opens,
            ImageUrl: null,
            FirstSeenUtc: now,
            Performers: [race.Name],
            Note: string.IsNullOrWhiteSpace(e.Registration) ? null : e.Registration.Trim());
    }

    static string Slug(string name) => SlugChars().Replace(name.ToLowerInvariant().Replace("ø", "oe").Replace("æ", "ae").Replace("å", "aa"), "-").Trim('-');

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex SlugChars();
}

/// <summary>Where sources keep their own small state files (under App_Data).</summary>
public record StatePath(string Runs);
