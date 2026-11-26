using System.Globalization;
using Kultur.Api.Auth;
using Kultur.Api.Events;
using Kultur.Api.Groups;

namespace Kultur.Api.Integration;

public record SummaryItem(string Title, string? Detail, string Date, string? Time, string? Url);
public record SummaryNote(string Text, string? Url);
public record Summary(List<SummaryItem> Items, List<SummaryNote> Notes, string? Badge);

// kaspert.dk's "I dag" list (contract in the kaspert repo, docs/summary-api.md), for one user
// (Integrations:Kaspert:User): the dates they're going to – saved (Gem/Måske) or in a chosen group
// ("Vælg til") – and ticket sales opening for those, from today through the next 7 days; the badge is
// how many groups wait in "Siden sidst". Read-only, behind its own key (policy "Summary").
public static class SummaryEndpoint
{
    public const int Days = 7;
    const string DefaultBaseUrl = "https://kultur.kaspert.dk";
    static readonly TimeZoneInfo Danish = TimeZoneInfo.FindSystemTimeZoneById("Europe/Copenhagen");

    public static void MapSummaryEndpoint(this IEndpointRouteBuilder app) =>
        app.MapGet("/api/integration/summary", async (EventJudge judge, TimeProvider clock, IConfiguration config, CancellationToken ct) =>
        {
            // Answer for a user a login can be, never a guess: without one, say so.
            var userId = config["Integrations:Kaspert:User"];
            if (string.IsNullOrEmpty(userId) || !Users.Known.Contains(userId))
                return Results.Problem("Integrations:Kaspert:User mangler eller er ikke en bruger.", statusCode: 503);

            var baseUrl = (config["Integrations:Kaspert:BaseUrl"] is { Length: > 0 } b ? b : DefaultBaseUrl).TrimEnd('/');
            var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), Danish).DateTime);
            var last = today.AddDays(Days);

            var j = await judge.JudgeAsync(userId, ct);
            return Results.Ok(Build(j, today, last, baseUrl));
        }).RequireAuthorization(ApiKeyHandler.SummaryPolicy);

    static Summary Build(Judgement j, DateOnly today, DateOnly last, string baseUrl)
    {
        var learning = j.Learning;
        string Link(string series) => $"{baseUrl}/group?key={Uri.EscapeDataString(series)}";
        bool InWindow(DateOnly d) => d >= today && d <= last;

        // Going: a date saved (Gem/Måske), or a date of a chosen group not turned down. A series
        // dismissed with "Nej tak alle" never shows, whatever its dates say.
        var going = j.Events
            .Select(x => (x.Item, Series: Series.KeyOf(x.Item.Event)))
            .Where(x => !learning.DismissedSeries.ContainsKey(x.Series))
            .Where(x => x.Item.Reaction is Reaction.Saved or Reaction.Maybe
                || (learning.ChosenSeries.ContainsKey(x.Series) && x.Item.Reaction != Reaction.NotInterested))
            .ToList();

        var items = new List<(DateOnly Date, string? Time, SummaryItem Item)>();
        foreach (var (item, series) in going)
        {
            var e = item.Event;
            if (e.Date is not { } date || !InWindow(date)) continue;
            var where = Place(e);
            var detail = item.Reaction == Reaction.Maybe ? (where is null ? "Måske" : $"Måske · {where}") : where;
            var time = e.Start is { } start ? TimeZoneInfo.ConvertTime(start, Danish).ToString("HH:mm", CultureInfo.InvariantCulture) : null;
            items.Add((date, time, new SummaryItem(e.Title, detail, Day(date), time, Link(series))));
        }

        // Ticket sales: once per series and opening time, however many of its dates go on sale then.
        foreach (var sale in going
            .Where(x => x.Item.Event.SaleStart is not null)
            .GroupBy(x => (x.Series, Start: x.Item.Event.SaleStart!.Value)))
        {
            var local = TimeZoneInfo.ConvertTime(sale.Key.Start, Danish);
            var date = DateOnly.FromDateTime(local.DateTime);
            if (!InWindow(date)) continue;
            var dates = sale.OrderBy(x => x.Item.Event.Date ?? DateOnly.MaxValue).ToList();
            var first = dates[0].Item.Event;
            var detail = dates.Count == 1 ? Place(first) : $"{dates.Count} datoer";
            var time = local.ToString("HH:mm", CultureInfo.InvariantCulture);
            items.Add((date, time, new SummaryItem($"Billetsalget åbner: {first.Title}", detail, Day(date), time, Link(sale.Key.Series))));
        }

        // "nye": the groups in "Siden sidst" that are new since the last visit (IsNew), not every undecided one.
        var fresh = GroupEndpoints.List(j, GroupView.Inbox).Count(g => g.IsNew);
        return new Summary(
            items.OrderBy(i => i.Date).ThenBy(i => i.Time ?? "").ThenBy(i => i.Item.Title, StringComparer.CurrentCulture).Select(i => i.Item).ToList(),
            [],
            fresh > 0 ? $"{fresh} nye" : null);
    }

    static string? Place(CultureEvent e) =>
        string.Join(", ", new[] { e.Venue, e.City }.Where(s => !string.IsNullOrWhiteSpace(s)).Distinct(StringComparer.OrdinalIgnoreCase))
            is { Length: > 0 } p ? p : null;

    static string Day(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
