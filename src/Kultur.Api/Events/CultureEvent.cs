namespace Kultur.Api.Events;

public enum Category { Music, Theatre, Comedy, Talk, Film, Sport, Running, Other }

/// <summary>How far away: the further, the stronger a match must be (see docs/koncept.md).</summary>
public enum Zone { Copenhagen = 1, Denmark = 2, Abroad = 3 }

public enum Reaction { Saved, Maybe, NotInterested }

/// <summary>Why "Nej tak": this place (only this date goes), or the event itself (every date goes).</summary>
public enum DismissReason { Place, Event }

/// <summary>
/// One event you can buy a ticket for, normalised from any source.
/// Id is "{source}:{the source's own id}", so the same event from one source is merged on rescans.
/// Performers: artists, bands or teams taking part (null in data saved before they were fetched).
/// Note: a short fact worth showing, e.g. how to get a place in a race.
/// Series: what ties the dates of one show or race together, when the source knows (see <see cref="Series"/>).
/// </summary>
public record CultureEvent(
    string Id,
    string Source,
    string Title,
    Category Category,
    string? Genre,
    DateOnly? Date,
    DateTimeOffset? Start,
    string? Venue,
    string? City,
    string? CountryCode,
    decimal? PriceFrom,
    string? Currency,
    string Url,
    DateTimeOffset? SaleStart,
    string? ImageUrl,
    DateTimeOffset FirstSeenUtc,
    IReadOnlyList<string>? Performers = null,
    string? Note = null,
    string? Series = null);

/// <summary>The dates of one show (a tour, a talk on the road, a race year after year) share a series key.</summary>
public static partial class Series
{
    /// <summary>The source's own grouping when it has one, otherwise the title without punctuation.</summary>
    public static string KeyOf(CultureEvent e) =>
        e.Series ?? "title:" + NotLetters().Replace(e.Title.ToLowerInvariant(), " ").Trim();

    [System.Text.RegularExpressions.GeneratedRegex(@"[^\p{L}\p{N}]+")]
    private static partial System.Text.RegularExpressions.Regex NotLetters();
}
