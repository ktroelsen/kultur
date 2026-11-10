using Kultur.Api.Taste;

namespace Kultur.Api.Events;

/// <summary>An event judged for one user. Hidden: a topic rated "aldrig".</summary>
public record Judged(EventItem Item, bool Hidden)
{
    /// <summary>Not to be suggested: hidden by a rating, or "Nej tak" (its own, its series' or its day's).</summary>
    public bool TurnedDown => Hidden || Item.Reaction == Reaction.NotInterested;
}

public record Judgement(List<Judged> Events, Learning Learning, DateTimeOffset? LastVisitUtc, bool UseZones);

// Every upcoming event as one user sees it: zone, reaction, score and reason. Shared by the event
// lists and the grouped lists.
public class EventJudge(EventCatalog catalog, UserData data, InterestStore interests)
{
    public async Task<Judgement> JudgeAsync(string userId, CancellationToken ct)
    {
        var lastVisit = (await data.StateAsync(userId, ct)).LastVisitUtc;
        var reactions = await data.ReactionsAsync(userId, ct);
        var learning = await data.LearningAsync(userId, ct);
        var learned = learning.Signals();
        var ratings = await interests.RatingsAsync(userId, ct);

        var judged = (await catalog.AllAsync(ct)).Select(e =>
        {
            var fit = TasteModel.FitOf(e, ratings, learned);
            // Dates of a series turned down, and everything on a busy day, count as "Nej tak" too –
            // also events found later. A reaction of the event's own always wins.
            Reaction? reaction = reactions.TryGetValue(e.Id, out var r) ? r
                : learning.DismissedSeries.ContainsKey(Series.KeyOf(e)) ? Reaction.NotInterested
                : e.Date is { } d && learning.BusyDays.ContainsKey(d.ToString("yyyy-MM-dd")) ? Reaction.NotInterested
                : null;
            var item = new EventItem(
                e,
                Zones.Of(e.City, e.CountryCode),
                reaction,
                lastVisit is null || e.FirstSeenUtc > lastVisit,
                fit.Score, fit.Because, fit.BecauseRating);
            return new Judged(item, fit.Hidden);
        }).ToList();

        return new Judgement(judged, learning, lastVisit, ratings.Count >= EventEndpoints.RatingsBeforeZoneThresholds);
    }
}
