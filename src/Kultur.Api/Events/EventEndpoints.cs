using System.Security.Claims;
using Kultur.Api.Auth;

namespace Kultur.Api.Events;

public enum EventView { New, ForYou, Saved }

public record EventItem(CultureEvent Event, Zone Zone, Reaction? Reaction, bool IsNew);

public record EventList(DateTimeOffset? LastVisitUtc, List<EventItem> Events);

public record ReactionRequest(Reaction? Reaction);

public static class EventEndpoints
{
    public const int MaxEvents = 200;

    public static void MapEventEndpoints(this IEndpointRouteBuilder app)
    {
        var events = app.MapGroup("/api/events").RequireAuthorization();

        // new = arrived since the last visit ("Siden sidst"), forYou = everything not turned down
        // (nearest zone first; Claude will rank this later), saved = Gem + Måske.
        events.MapGet("/", async (string? view, ClaimsPrincipal user, EventCatalog catalog, UserData data, CancellationToken ct) =>
        {
            var parsed = EventView.ForYou;
            if (view is not null && !Enum.TryParse(view, ignoreCase: true, out parsed))
                return Results.Problem("Ukendt visning.", statusCode: 400);

            var userId = Users.Id(user);
            var lastVisit = (await data.StateAsync(userId, ct)).LastVisitUtc;
            var reactions = await data.ReactionsAsync(userId, ct);

            var items = (await catalog.AllAsync(ct)).Select(e => new EventItem(
                e,
                Zones.Of(e.City, e.CountryCode),
                reactions.TryGetValue(e.Id, out var r) ? r : null,
                lastVisit is null || e.FirstSeenUtc > lastVisit));

            items = parsed switch
            {
                EventView.New => items.Where(i => i.IsNew && i.Reaction != Reaction.NotInterested),
                EventView.Saved => items.Where(i => i.Reaction is Reaction.Saved or Reaction.Maybe),
                _ => items.Where(i => i.Reaction != Reaction.NotInterested).OrderBy(i => i.Zone),
            };
            return Results.Ok(new EventList(lastVisit, items.Take(MaxEvents).ToList()));
        });

        events.MapPost("/seen", async (ClaimsPrincipal user, UserData data, CancellationToken ct) =>
        {
            await data.MarkSeenAsync(Users.Id(user), ct);
            return Results.NoContent();
        });

        // Gem / Måske / Nej tak; null takes the reaction back.
        events.MapPut("/{id}/reaction", async (string id, ReactionRequest body, ClaimsPrincipal user,
            EventCatalog catalog, UserData data, CancellationToken ct) =>
        {
            if (await catalog.GetAsync(id, ct) is null) return Results.NotFound();
            await data.SetReactionAsync(Users.Id(user), id, body.Reaction, ct);
            return Results.NoContent();
        });
    }
}
