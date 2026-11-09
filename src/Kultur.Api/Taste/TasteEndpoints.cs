using System.Security.Claims;
using Kultur.Api.Auth;
using Kultur.Api.Events;

namespace Kultur.Api.Taste;

public record TopicExample(string Title, DateOnly? Date, string? Venue);

/// <summary>The next topic to rate, with how many upcoming events it covers and a few of them.</summary>
public record NextTopic(Topic Topic, int Upcoming, List<TopicExample> Examples, int Rated, int Left);

public record RatingRequest(TopicKind Kind, string Name, Interest? Rating);

public record RatedTopicItem(string Key, TopicKind Kind, string Name, Interest Rating, DateTimeOffset AtUtc);

// "Smag": one topic at a time, rated from "aldrig" to "meget". The ratings decide what "Til dig" shows.
public static class TasteEndpoints
{
    public const int MaxNameLength = 200;
    public const int ExampleCount = 3;
    static readonly TimeSpan AskSkippedAgainAfter = TimeSpan.FromDays(7);

    // How often each kind comes up: performers twice as often, they say the most.
    static readonly Dictionary<TopicKind, int> Share = new()
    {
        [TopicKind.Genre] = 1, [TopicKind.Performer] = 2, [TopicKind.Venue] = 1,
    };

    public static void MapTasteEndpoints(this IEndpointRouteBuilder app)
    {
        var taste = app.MapGroup("/api/taste").RequireAuthorization();

        taste.MapGet("/next", async (ClaimsPrincipal user, EventCatalog catalog, InterestStore store, TimeProvider clock, CancellationToken ct) =>
        {
            var all = await store.AllAsync(Users.Id(user), ct);
            var next = Next(await catalog.AllAsync(ct), all, clock.GetUtcNow());
            return next is null ? Results.NoContent() : Results.Ok(next);
        });

        taste.MapGet("/", async (ClaimsPrincipal user, InterestStore store, CancellationToken ct) =>
            Results.Ok((await store.AllAsync(Users.Id(user), ct))
                .Where(kv => kv.Value.Rating is not null)
                .Select(kv => new RatedTopicItem(kv.Key, kv.Value.Kind, kv.Value.Name, kv.Value.Rating!.Value, kv.Value.AtUtc))
                .OrderByDescending(t => t.AtUtc)
                .ToList()));

        // Rate a topic; rating null = "spring over".
        taste.MapPut("/", async (RatingRequest body, ClaimsPrincipal user, InterestStore store, CancellationToken ct) =>
        {
            var name = body.Name?.Trim() ?? "";
            if (name.Length is 0 or > MaxNameLength) return Results.Problem("Ugyldigt navn.", statusCode: 400);
            if (body.Rating is { } r && !Enum.IsDefined(r)) return Results.Problem("Ugyldig vurdering.", statusCode: 400);
            await store.SetAsync(Users.Id(user), new Topic(body.Kind, name), body.Rating, ct);
            return Results.NoContent();
        });

        taste.MapDelete("/{key}", async (string key, ClaimsPrincipal user, InterestStore store, CancellationToken ct) =>
            await store.RemoveAsync(Users.Id(user), key, ct) ? Results.NoContent() : Results.NotFound());
    }

    /// <summary>
    /// Picks the next topic: first the broad categories, then genres, performers and venues in turn,
    /// each time the one with the most upcoming events. Events already turned down don't count, so
    /// after "Sport: aldrig" you aren't asked about football clubs. Skipped topics come back after a week.
    /// </summary>
    public static NextTopic? Next(List<CultureEvent> events, Dictionary<string, RatedTopic> asked, DateTimeOffset now)
    {
        var ratings = asked.Where(kv => kv.Value.Rating is not null).ToDictionary(kv => kv.Key, kv => kv.Value.Rating!.Value);
        var visible = events.Where(e => !TasteModel.FitOf(e, ratings).Hidden).ToList();

        var candidates = visible
            .SelectMany(e => TasteModel.TopicsOf(e).Select(t => (Topic: t, Event: e)))
            .GroupBy(x => x.Topic.Key)
            .Select(g => (Key: g.Key, Topic: g.First().Topic, Events: g.Select(x => x.Event).DistinctBy(e => e.Id).ToList()))
            .Where(c => !asked.TryGetValue(c.Key, out var a) || (a.Rating is null && now - a.AtUtc >= AskSkippedAgainAfter))
            .ToList();
        if (candidates.Count == 0) return null;

        var categories = candidates.Where(c => c.Topic.Kind == TopicKind.Category).ToList();
        var pool = categories.Count > 0 ? categories : candidates;
        if (categories.Count == 0)
        {
            // The kind asked least, relative to its share, goes next.
            var askedPerKind = asked.Values.GroupBy(a => a.Kind).ToDictionary(g => g.Key, g => g.Count());
            var kind = pool.Select(c => c.Topic.Kind).Distinct()
                .OrderBy(k => (double)askedPerKind.GetValueOrDefault(k) / Share.GetValueOrDefault(k, 1))
                .ThenBy(k => k)
                .First();
            pool = pool.Where(c => c.Topic.Kind == kind).ToList();
        }

        var pick = pool
            .OrderByDescending(c => c.Events.Count)
            .ThenBy(c => c.Topic.Name, StringComparer.CurrentCulture)
            .First();
        var examples = pick.Events
            .OrderBy(e => e.Date ?? DateOnly.MaxValue)
            .Take(ExampleCount)
            .Select(e => new TopicExample(e.Title, e.Date, e.Venue))
            .ToList();
        return new NextTopic(pick.Topic, pick.Events.Count, examples,
            Rated: ratings.Count, Left: candidates.Count);
    }
}
