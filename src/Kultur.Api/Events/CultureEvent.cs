namespace Kultur.Api.Events;

public enum Category { Music, Theatre, Comedy, Film, Sport, Other }

/// <summary>How far away: the further, the stronger a match must be (see docs/koncept.md).</summary>
public enum Zone { Copenhagen = 1, Denmark = 2, Abroad = 3 }

public enum Reaction { Saved, Maybe, NotInterested }

/// <summary>
/// One event you can buy a ticket for, normalised from any source.
/// Id is "{source}:{the source's own id}", so the same event from one source is merged on rescans.
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
    DateTimeOffset FirstSeenUtc);
