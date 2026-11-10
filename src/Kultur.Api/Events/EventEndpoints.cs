using System.Security.Claims;
using Kultur.Api.Auth;
using Kultur.Api.Taste;

namespace Kultur.Api.Events;

public enum EventView { New, ForYou, Saved }

/// <summary>An event as one user sees it. Score and Because come from the ratings under "Smag".</summary>
public record EventItem(CultureEvent Event, Zone Zone, Reaction? Reaction, bool IsNew, int Score, Topic? Because, Interest? BecauseRating);

public record EventList(DateTimeOffset? LastVisitUtc, List<EventItem> Events);

/// <summary>Reason only goes with NotInterested: Date hides the whole day, Event every date of the event.</summary>
public record ReactionRequest(Reaction? Reaction, DismissReason? Reason = null);

public static class EventEndpoints
{
    public const int MaxEvents = 200;

    /// <summary>Below this many ratings the zone thresholds would hide almost everything, so they wait.</summary>
    public const int RatingsBeforeZoneThresholds = 10;

    /// <summary>The further away, the better the match must be (docs/koncept.md: afstandszoner).</summary>
    public static int MinScore(Zone zone) => zone switch
    {
        Zone.Copenhagen => int.MinValue,
        Zone.Denmark => 2,
        _ => 4,
    };

    public static void MapEventEndpoints(this IEndpointRouteBuilder app)
    {
        var events = app.MapGroup("/api/events").RequireAuthorization();

        // new = arrived since the last visit ("Siden sidst"), forYou = what fits the ratings (nearest
        // zone first, best match first), saved = Gem + Måske. Events turned down – "Nej tak", or a
        // topic rated "aldrig" – are left out of new and forYou.
        events.MapGet("/", async (string? view, ClaimsPrincipal user, EventJudge judge, CancellationToken ct) =>
        {
            var parsed = EventView.ForYou;
            if (view is not null && !Enum.TryParse(view, ignoreCase: true, out parsed))
                return Results.Problem("Ukendt visning.", statusCode: 400);

            var judgement = await judge.JudgeAsync(Users.Id(user), ct);
            var (judged, lastVisit, useZones) = (judgement.Events, judgement.LastVisitUtc, judgement.UseZones);

            static bool TurnedDown(Judged x) => x.TurnedDown;

            var items = parsed switch
            {
                EventView.New => judged.Where(x => x.Item.IsNew && !TurnedDown(x)).Select(x => x.Item),
                EventView.Saved => judged.Where(x => x.Item.Reaction is Reaction.Saved or Reaction.Maybe).Select(x => x.Item),
                _ => judged
                    .Where(x => !TurnedDown(x))
                    .Where(x => !useZones || x.Item.Reaction is not null || x.Item.Score >= MinScore(x.Item.Zone))
                    .Select(x => x.Item)
                    .OrderBy(i => i.Zone)
                    .ThenByDescending(i => i.Score),
            };
            return Results.Ok(new EventList(lastVisit, items.Take(MaxEvents).ToList()));
        });

        events.MapPost("/seen", async (ClaimsPrincipal user, UserData data, CancellationToken ct) =>
        {
            await data.MarkSeenAsync(Users.Id(user), ct);
            return Results.NoContent();
        });

        // Gem / Måske / Nej tak (with why); null takes the reaction back.
        events.MapPut("/{id}/reaction", async (string id, ReactionRequest body, ClaimsPrincipal user,
            EventCatalog catalog, UserData data, CancellationToken ct) =>
        {
            if (await catalog.GetAsync(id, ct) is not { } e) return Results.NotFound();
            if (body.Reason is not null && body.Reaction != Reaction.NotInterested)
                return Results.Problem("En begrundelse hører kun til \"Nej tak\".", statusCode: 400);
            if (body.Reason == DismissReason.Place)
                return Results.Problem("\"Stedet\" er ikke længere en begrundelse.", statusCode: 400);
            await data.SetReactionAsync(Users.Id(user), e, body.Reaction, body.Reason, ct);
            return Results.NoContent();
        });
    }
}
