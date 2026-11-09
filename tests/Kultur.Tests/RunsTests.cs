using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Kultur.Api.Events;
using Kultur.Api.Scan;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Kultur.Tests;

// Stand-in for the Messages API: answers each request with Answer(race name from the prompt).
public partial class FakeClaude : HttpMessageHandler
{
    public Func<string, string> Answer { get; set; } = _ => """{"editions": []}""";
    public List<JsonObject> Requests { get; } = [];

    [GeneratedRegex(@"next editions of (.+?) \(")]
    private static partial Regex RaceName();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
    {
        var body = JsonNode.Parse(await req.Content!.ReadAsStringAsync(ct))!.AsObject();
        lock (Requests) Requests.Add(body);
        var prompt = body["messages"]![0]!["content"]!.ToString();
        var race = RaceName().Match(prompt).Groups[1].Value;
        var message = new Dictionary<string, object?>
        {
            ["id"] = $"msg_{Guid.NewGuid():n}", ["type"] = "message", ["role"] = "assistant", ["model"] = "claude-opus-5-5",
            ["content"] = new object[] { new { type = "text", text = Answer(race), citations = (object?)null } },
            ["stop_reason"] = "end_turn", ["stop_sequence"] = null, ["stop_details"] = null, ["container"] = null,
            ["context_management"] = null,
            ["usage"] = new Dictionary<string, object?>
            {
                ["input_tokens"] = 100, ["output_tokens"] = 20, ["cache_creation_input_tokens"] = 0, ["cache_read_input_tokens"] = 0,
                ["cache_creation"] = null, ["server_tool_use"] = null, ["service_tier"] = "standard",
            },
        };
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(message), Encoding.UTF8, "application/json"),
        };
    }
}

public class RunsTests
{
    static (KulturFactory Factory, WebApplicationFactory<Program> App, FakeClaude Claude) WithClaude()
    {
        var factory = new KulturFactory();
        var claude = new FakeClaude();
        var app = factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("Ai:ApiKey", "sk-test");
            b.UseSetting("Ai:BaseUrl", "https://anthropic.test");
            b.ConfigureTestServices(s => s.AddHttpClient("anthropic").ConfigurePrimaryHttpMessageHandler(() => claude));
        });
        return (factory, app, claude);
    }

    static string Editions(params object[] editions) => "Her er det:\n```json\n" + JsonSerializer.Serialize(new { editions }) + "\n```";

    static async Task<HttpClient> LoginAsync(WebApplicationFactory<Program> app)
    {
        var client = app.CreateClient();
        (await client.PostAsJsonAsync("/api/auth/login", new { password = KulturFactory.Password })).EnsureSuccessStatusCode();
        return client;
    }

    [Fact]
    public async Task Each_race_is_looked_up_with_web_search_and_becomes_a_run()
    {
        var (factory, app, claude) = WithClaude();
        using var _ = factory;
        using var __ = app;
        claude.Answer = race => race switch
        {
            "Berlin Marathon" => Editions(
                new { name = "Berlin Marathon", year = 2027, date = "2027-09-26", city = "Berlin", countryCode = "DE", distance = "Marathon",
                      url = "https://www.bmw-berlin-marathon.com/", registrationOpens = "2099-10-01", registration = "Lodtrækning i oktober." },
                new { name = "Berlin Marathon", year = 2000, date = "2000-09-24", city = "Berlin", countryCode = "DE", distance = "Marathon",
                      url = "https://x", registrationOpens = (string?)null, registration = (string?)null }),
            "Royal Run" => Editions(
                new { name = "Royal Run", year = 2099, date = (string?)null, city = "København", countryCode = "DK", distance = "10 km",
                      url = "not a url", registrationOpens = (string?)null, registration = "Først til mølle." },
                new { name = "Et andet løb", year = 2099, date = "2099-06-01", city = "X", countryCode = "DK", distance = "5 km",
                      url = "https://x", registrationOpens = (string?)null, registration = (string?)null }),
            _ => """{"editions": []}""",
        };

        var client = await LoginAsync(app);
        var result = await (await client.PostAsync("/api/scan", null)).Content.ReadAsync<ScanResult>();
        var runs = result!.Sources.Single(s => s.Source == "løb");
        Assert.Null(runs.Error);
        Assert.Equal(2, runs.Fetched); // the past edition and the race not on the list are dropped

        Assert.Equal(new RunsOptions().Races.Count, claude.Requests.Count);
        var request = claude.Requests[0];
        Assert.Equal("claude-opus-5-5", request["model"]!.GetValue<string>());
        var tool = request["tools"]![0]!;
        Assert.Equal("web_search_20260209", tool["type"]!.GetValue<string>());
        Assert.Equal(5, tool["max_uses"]!.GetValue<int>());

        var events = (await client.GetAsync<EventList>("/api/events"))!.Events;
        var berlin = events.Single(i => i.Event.Id == "runs:berlin-marathon-2027");
        Assert.Equal("Berlin Marathon 2027", berlin.Event.Title);
        Assert.Equal(Category.Running, berlin.Event.Category);
        Assert.Equal(new DateOnly(2027, 9, 26), berlin.Event.Date);
        Assert.Equal(Zone.Abroad, berlin.Zone);
        Assert.Equal(new DateTimeOffset(2099, 10, 1, 0, 0, 0, TimeSpan.Zero), berlin.Event.SaleStart);
        Assert.Equal("Lodtrækning i oktober.", berlin.Event.Note);
        Assert.Equal(["Berlin Marathon"], berlin.Event.Performers!);

        var royal = events.Single(i => i.Event.Id == "runs:royal-run-2099");
        Assert.Null(royal.Event.Date);
        Assert.StartsWith("https://www.google.com/search?q=", royal.Event.Url); // no usable official link
    }

    [Fact]
    public async Task Races_are_looked_up_at_most_once_a_week()
    {
        var (factory, app, claude) = WithClaude();
        using var _ = factory;
        using var __ = app;
        claude.Answer = race => race == "Berlin Marathon"
            ? Editions(new { name = "Berlin Marathon", year = 2099, date = "2099-09-27", city = "Berlin", countryCode = "DE",
                             distance = "Marathon", url = "https://x.example/", registrationOpens = (string?)null, registration = (string?)null })
            : """{"editions": []}""";

        var client = await LoginAsync(app);
        await client.PostAsync("/api/scan", null);
        var first = claude.Requests.Count;
        await client.PostAsync("/api/scan", null);
        Assert.Equal(first, claude.Requests.Count);
        Assert.Contains((await client.GetAsync<EventList>("/api/events"))!.Events, i => i.Event.Id == "runs:berlin-marathon-2099");
    }

    [Fact]
    public async Task Without_a_key_runs_are_skipped()
    {
        using var factory = new KulturFactory();
        var client = await factory.LoggedInClientAsync();
        var result = await (await client.PostAsync("/api/scan", null)).Content.ReadAsync<ScanResult>();
        Assert.NotNull(result!.Sources.Single(s => s.Source == "løb").Error);
    }

    [Theory]
    [InlineData("""{"editions": [{"name": "Royal Run", "year": 2099}]}""", 1)]
    [InlineData("Svar:\n```json\n{\"editions\": []}\n```", 0)]
    public void Editions_are_read_from_the_answer(string text, int count) =>
        Assert.Equal(count, RunsSource.ParseEditions(text).Count);

    [Theory]
    [InlineData("Jeg kunne ikke finde noget.")]
    [InlineData("{\"something\": 1}")]
    [InlineData("{ not json }")]
    public void A_bad_answer_is_an_error(string text) =>
        Assert.Throws<HttpRequestException>(() => RunsSource.ParseEditions(text));
}
