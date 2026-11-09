using System.Net;
using System.Net.Http.Json;
using System.Text;
using Kultur.Api.Scan;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Kultur.Tests;

// Stand-in for Ticketmaster: answers every request with Body, and records the URLs asked for.
public class FakeTicketmaster : HttpMessageHandler
{
    public string Body { get; set; } = TicketmasterJson.Page();
    public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;
    public List<string> Requested { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
    {
        lock (Requested) Requested.Add(req.RequestUri!.ToString());
        return Task.FromResult(new HttpResponseMessage(Status) { Content = new StringContent(Body, Encoding.UTF8, "application/json") });
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

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("Storage:DataDir", _dataDir);
        builder.UseSetting("Auth:Password", Password);
        builder.UseSetting("Auth:LoginAttemptsPerMinute", "1000");
        builder.UseSetting("Scan:Key", ScanKey);
        builder.UseSetting("Ticketmaster:ApiKey", "tm-key");
        builder.UseSetting("Ticketmaster:Pages", "1");
        builder.ConfigureTestServices(s => s.AddHttpClient<TicketmasterSource>().ConfigurePrimaryHttpMessageHandler(() => Ticketmaster));
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
