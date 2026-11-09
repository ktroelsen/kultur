namespace Kultur.Api.Events;

// All known upcoming events from every source, in one JSON file (App_Data/events.json).
// Shared by all users; what each user thinks of them lives in UserData.
public class EventCatalog(string path, TimeProvider clock)
{
    readonly SemaphoreSlim _lock = new(1, 1);

    public async Task<List<CultureEvent>> AllAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try { return await LoadAsync(ct); }
        finally { _lock.Release(); }
    }

    public async Task<CultureEvent?> GetAsync(string id, CancellationToken ct = default) =>
        (await AllAsync(ct)).FirstOrDefault(e => e.Id == id);

    /// <summary>
    /// Adds new events and refreshes known ones (keeping when they were first seen), and drops
    /// events that are over. Returns how many were new.
    /// </summary>
    public async Task<int> MergeAsync(IEnumerable<CultureEvent> fresh, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            var byId = (await LoadAsync(ct)).ToDictionary(e => e.Id);
            var added = 0;
            foreach (var e in fresh)
            {
                if (byId.TryGetValue(e.Id, out var known))
                    byId[e.Id] = e with { FirstSeenUtc = known.FirstSeenUtc };
                else
                {
                    byId[e.Id] = e;
                    added++;
                }
            }

            var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
            var upcoming = byId.Values
                .Where(e => e.Date is null || e.Date >= today)
                .OrderBy(e => e.Date ?? DateOnly.MaxValue)
                .ThenBy(e => e.Title, StringComparer.CurrentCulture)
                .ToList();
            await JsonFile.WriteAsync(path, upcoming, ct);
            return added;
        }
        finally { _lock.Release(); }
    }

    async Task<List<CultureEvent>> LoadAsync(CancellationToken ct) =>
        await JsonFile.ReadAsync<List<CultureEvent>>(path, ct) ?? [];
}
