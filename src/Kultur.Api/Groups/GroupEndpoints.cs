using System.Security.Claims;
using Kultur.Api.Auth;
using Kultur.Api.Events;
using Kultur.Api.Taste;

namespace Kultur.Api.Groups;

public enum GroupView { Inbox, Chosen }

/// <summary>
/// All dates of one event (a show on tour, a talk around the country, a race) as one entry, from the
/// dates still in play: date range, how many, where, the lowest price. Key is the series key.
/// </summary>
public record EventGroup(
    string Key, string Title, Category Category, string? Genre, IReadOnlyList<string>? Performers,
    DateOnly? From, DateOnly? To, int Dates, List<string> Cities, decimal? PriceFrom, string? Currency,
    string? ImageUrl, string? Note, DateTimeOffset? SaleStart, Zone Zone, bool IsNew, int Score,
    Topic? Because, Interest? BecauseRating, bool Chosen);

public record GroupList(DateTimeOffset? LastVisitUtc, List<EventGroup> Groups);

/// <summary>A group and every one of its dates – also those turned down, so they can be brought back.</summary>
public record GroupDetail(EventGroup? Group, string Key, bool Chosen, bool Dismissed, List<EventItem> Events);

public record ChoiceRequest(string Key, SeriesChoice? Choice);

// "Siden sidst" (inbox) shows every undecided event grouped, new ones first; "Vælg til" moves a group
// to "Til dig" (chosen). Opening a group lists its dates, each of which can be saved or turned down.
public static class GroupEndpoints
{
    public const int MaxGroups = 200;
    const int MaxCities = 5;

    public static void MapGroupEndpoints(this IEndpointRouteBuilder app)
    {
        var groups = app.MapGroup("/api/groups").RequireAuthorization();

        groups.MapGet("/", async (string? view, ClaimsPrincipal user, EventJudge judge, CancellationToken ct) =>
        {
            var parsed = GroupView.Inbox;
            if (view is not null && !Enum.TryParse(view, ignoreCase: true, out parsed))
                return Results.Problem("Ukendt visning.", statusCode: 400);

            var j = await judge.JudgeAsync(Users.Id(user), ct);
            var result = j.Events
                .GroupBy(x => Series.KeyOf(x.Item.Event))
                .Select(g => Summarize(g.Key, g.ToList(), j.Learning))
                .OfType<EventGroup>();

            result = parsed == GroupView.Chosen
                ? result.Where(g => g.Chosen).OrderBy(g => g.From ?? DateOnly.MaxValue)
                : result
                    .Where(g => !g.Chosen)
                    .Where(g => !j.UseZones || g.Score >= EventEndpoints.MinScore(g.Zone))
                    .OrderByDescending(g => g.IsNew)
                    .ThenBy(g => g.Zone)
                    .ThenByDescending(g => g.Score)
                    .ThenBy(g => g.From ?? DateOnly.MaxValue);
            return Results.Ok(new GroupList(j.LastVisitUtc, result.Take(MaxGroups).ToList()));
        });

        groups.MapGet("/events", async (string key, ClaimsPrincipal user, EventJudge judge, CancellationToken ct) =>
        {
            var j = await judge.JudgeAsync(Users.Id(user), ct);
            var dates = j.Events.Where(x => Series.KeyOf(x.Item.Event) == key).ToList();
            if (dates.Count == 0) return Results.NotFound();
            return Results.Ok(new GroupDetail(
                Summarize(key, dates, j.Learning),
                key,
                j.Learning.ChosenSeries.ContainsKey(key),
                j.Learning.DismissedSeries.ContainsKey(key),
                dates.Select(x => x.Item).OrderBy(i => i.Event.Date ?? DateOnly.MaxValue).ThenBy(i => i.Event.Start).ToList()));
        });

        groups.MapPut("/choice", async (ChoiceRequest body, ClaimsPrincipal user, EventCatalog catalog, UserData data, CancellationToken ct) =>
        {
            var dates = (await catalog.AllAsync(ct)).Where(e => Series.KeyOf(e) == body.Key).ToList();
            if (dates.Count == 0) return Results.NotFound();
            await data.SetSeriesChoiceAsync(Users.Id(user), body.Key, dates[0], body.Choice, ct);
            return Results.NoContent();
        });
    }

    /// <summary>
    /// The group from its dates still in play: for a chosen group the ones not turned down by a "Nej tak";
    /// otherwise also not hidden by a rating, and the series not turned down. Null when none are left.
    /// </summary>
    static EventGroup? Summarize(string key, List<Judged> all, Learning learning)
    {
        var chosen = learning.ChosenSeries.ContainsKey(key);
        if (!chosen && learning.DismissedSeries.ContainsKey(key)) return null;
        var live = all
            .Where(x => chosen ? x.Item.Reaction != Reaction.NotInterested : !x.TurnedDown)
            .OrderBy(x => x.Item.Event.Date ?? DateOnly.MaxValue)
            .Select(x => x.Item)
            .ToList();
        if (live.Count == 0) return null;

        var first = live[0];
        var best = live.MaxBy(i => i.Score)!;
        var priced = live.Where(i => i.Event.PriceFrom is not null).MinBy(i => i.Event.PriceFrom);
        var now = DateTimeOffset.UtcNow;
        return new EventGroup(
            key,
            first.Event.Title,
            first.Event.Category,
            live.Select(i => i.Event.Genre).FirstOrDefault(g => g is not null),
            live.SelectMany(i => i.Event.Performers ?? []).Distinct(StringComparer.OrdinalIgnoreCase).ToList() is { Count: > 0 } p ? p : null,
            live.Select(i => i.Event.Date).FirstOrDefault(d => d is not null),
            live.Select(i => i.Event.Date).LastOrDefault(d => d is not null),
            live.Count,
            live.Select(i => i.Event.City).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).Take(MaxCities).ToList(),
            priced?.Event.PriceFrom,
            priced?.Event.Currency,
            live.Select(i => i.Event.ImageUrl).FirstOrDefault(u => u is not null),
            live.Select(i => i.Event.Note).FirstOrDefault(n => n is not null),
            live.Select(i => i.Event.SaleStart).Where(s => s > now).Min(),
            live.Min(i => i.Zone),
            live.Any(i => i.IsNew),
            best.Score,
            best.Because,
            best.BecauseRating,
            chosen);
    }
}
