using System.Security.Cryptography;
using System.Text;
using Kultur.Api.Events;

namespace Kultur.Api.Scan;

public record SourceResult(string Source, int Fetched, string? Error);

public record ScanResult(List<SourceResult> Sources, int Added, int Total);

/// <summary>Only one scan at a time (the cron and a user could start one together).</summary>
public class ScanGate
{
    public SemaphoreSlim Running { get; } = new(1, 1);
}

// The nightly scan. IIS recycles the app pool, so there is no background service: a GitHub
// Actions cron calls POST /api/scan with the key in the X-Scan-Key header (Scan:Key). A signed-in
// user may also start it.
public static class ScanEndpoints
{
    public const string KeyHeader = "X-Scan-Key";

    public static void MapScanEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/scan", async (HttpContext http, IConfiguration config, IEnumerable<IEventSource> sources,
            EventCatalog catalog, ScanGate gate, TimeProvider clock, ILoggerFactory logs, CancellationToken ct) =>
        {
            if (http.User.Identity?.IsAuthenticated != true && !KeyMatches(http.Request.Headers[KeyHeader], config["Scan:Key"]))
                return Results.Unauthorized();
            if (!await gate.Running.WaitAsync(0, ct))
                return Results.Problem("Der kører allerede en scanning.", statusCode: 409);

            try
            {
                var log = logs.CreateLogger("Scan");
                var now = clock.GetUtcNow();
                var results = new List<SourceResult>();
                var fresh = new List<CultureEvent>();
                foreach (var source in sources)
                {
                    if (!source.Configured)
                    {
                        results.Add(new SourceResult(source.Name, 0, "Ikke sat op (mangler nøgle)."));
                        continue;
                    }
                    try
                    {
                        var events = await source.FetchAsync(now, ct);
                        fresh.AddRange(events);
                        results.Add(new SourceResult(source.Name, events.Count, null));
                    }
                    catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
                    {
                        // One failing source must not stop the others.
                        log.LogWarning(ex, "Source {Source} failed", source.Name);
                        results.Add(new SourceResult(source.Name, 0, ex.Message));
                    }
                }
                var added = await catalog.MergeAsync(fresh, ct);
                return Results.Ok(new ScanResult(results, added, (await catalog.AllAsync(ct)).Count));
            }
            finally
            {
                gate.Running.Release();
            }
        });
    }

    static bool KeyMatches(string? given, string? expected) =>
        !string.IsNullOrEmpty(expected) && !string.IsNullOrEmpty(given) &&
        CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(given)),
            SHA256.HashData(Encoding.UTF8.GetBytes(expected)));
}
