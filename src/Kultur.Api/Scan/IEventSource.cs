using Kultur.Api.Events;

namespace Kultur.Api.Scan;

/// <summary>A fixed source of events (an API, later also pages read by Claude).</summary>
public interface IEventSource
{
    string Name { get; }

    /// <summary>False when the source lacks its key; the scan then skips it.</summary>
    bool Configured { get; }

    Task<List<CultureEvent>> FetchAsync(DateTimeOffset now, CancellationToken ct);

    /// <summary>
    /// Id prefixes the last fetch covered completely: catalog events under them that weren't in it are
    /// gone (e.g. a venue's programme read afresh). Empty: the fetch only adds and updates.
    /// </summary>
    IReadOnlyList<string> Replaces => [];
}
