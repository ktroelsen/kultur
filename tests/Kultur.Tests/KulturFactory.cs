using System.Net;
using System.Net.Http.Json;
using System.Text;
using Kultur.Api.Scan;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Kultur.Tests;

// Stand-in for Ticketmaster: answers with Body (or Respond, when set, per request) and records the URLs asked for.
public class FakeTicketmaster : HttpMessageHandler
{
    public string Body { get; set; } = TicketmasterJson.Page();
    public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;
    public Func<Uri, string>? Respond { get; set; }
    public List<string> Requested { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
    {
        lock (Requested) Requested.Add(req.RequestUri!.ToString());
        var body = Respond?.Invoke(req.RequestUri!) ?? Body;
        return Task.FromResult(new HttpResponseMessage(Status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
    }
}

// Stand-in for www.originaltalks.dk: fixed pages by URL, everything else 404.
public class FakeSite : HttpMessageHandler
{
    public Dictionary<string, string> Pages { get; } = [];
    public List<string> Requested { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
    {
        var url = req.RequestUri!.ToString();
        lock (Requested) Requested.Add(url);
        return Task.FromResult(Pages.TryGetValue(url, out var body)
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "text/html") }
            : new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("") });
    }
}

// Boots the real API against a throwaway App_Data dir with a known password and scan key,
// and a fake Ticketmaster.
public class KulturFactory : WebApplicationFactory<Program>
{
    public const string Password = "test-password";
    public const string ScanKey = "test-scan-key";

    readonly string _dataDir = Path.Combine(Path.GetTempPath(), "kultur-tests", Guid.NewGuid().ToString("n"));

    public FakeTicketmaster Ticketmaster { get; } = new();
    public FakeSite OriginalTalks { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("Storage:DataDir", _dataDir);
        builder.UseSetting("Auth:Password", Password);
        builder.UseSetting("Auth:LoginAttemptsPerMinute", "1000");
        builder.UseSetting("Scan:Key", ScanKey);
        builder.UseSetting("Ticketmaster:ApiKey", "tm-key");
        builder.UseSetting("Ticketmaster:Months", "1");
        builder.UseSetting("Ticketmaster:DelayMs", "0");
        builder.UseSetting("OriginalTalks:DelayMs", "0");
        builder.ConfigureTestServices(s =>
        {
            s.AddHttpClient<TicketmasterSource>().ConfigurePrimaryHttpMessageHandler(() => Ticketmaster);
            s.AddHttpClient<OriginalTalksSource>().ConfigurePrimaryHttpMessageHandler(() => OriginalTalks);
        });
    }

    public async Task<HttpClient> LoggedInClientAsync()
    {
        var client = CreateClient();
        var res = await client.PostAsJsonAsync("/api/auth/login", new { password = Password });
        res.EnsureSuccessStatusCode();
        return client;
    }

    public async Task ScanAsync()
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/scan");
        req.Headers.Add(ScanEndpoints.KeyHeader, ScanKey);
        (await CreateClient().SendAsync(req)).EnsureSuccessStatusCode();
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        try { Directory.Delete(_dataDir, recursive: true); } catch (IOException) { /* best effort */ }
    }
}
