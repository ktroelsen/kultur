using Kultur.Api.Events;

namespace Kultur.Api.Scan;

/// <summary>A fixed source of events (an API, later also pages read by Claude).</summary>
public interface IEventSource
{
    string Name { get; }

    /// <summary>False when the source lacks its key; the scan then skips it.</summary>
    bool Configured { get; }

    Task<List<CultureEvent>> FetchAsync(DateTimeOffset now, CancellationToken ct);
}
