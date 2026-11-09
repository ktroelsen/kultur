using System.Reflection;
using System.Threading.RateLimiting;
using Kultur.Api.Auth;
using Kultur.Api.Events;
using Kultur.Api.Profile;
using Kultur.Api.Scan;
using Kultur.Api.Taste;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

// Everything the app writes lives under App_Data. The deploy skips App_Data (msdeploy -skip),
// so it survives redeploys on Simply. Override with Storage__DataDir (tests use a temp dir).
var dataDir = builder.Configuration["Storage:DataDir"]
    ?? Path.Combine(builder.Environment.ContentRootPath, "App_Data");
Directory.CreateDirectory(dataDir);
var usersDir = Path.Combine(dataDir, "users");

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(sp => new EventCatalog(Path.Combine(dataDir, "events.json"), sp.GetRequiredService<TimeProvider>()));
builder.Services.AddSingleton(sp => new UserData(usersDir, sp.GetRequiredService<TimeProvider>()));
builder.Services.AddSingleton(sp => new InterestStore(usersDir, sp.GetRequiredService<TimeProvider>()));

// Event sources, scanned nightly (POST /api/scan). Each is skipped while it lacks its key.
builder.Services.AddSingleton<ScanGate>();
builder.Services.Configure<TicketmasterOptions>(builder.Configuration.GetSection("Ticketmaster"));
builder.Services.AddHttpClient<TicketmasterSource>(c => c.Timeout = TimeSpan.FromSeconds(30));
builder.Services.AddTransient<IEventSource>(sp => sp.GetRequiredService<TicketmasterSource>());

builder.Services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));

// Persist cookie-encryption keys, otherwise every IIS app-pool recycle logs you out.
builder.Services.AddDataProtection()
    .SetApplicationName("Kultur")
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(dataDir, "keys")));

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.Cookie.Name = "kultur_auth";
        o.Cookie.HttpOnly = true;
        o.Cookie.SameSite = SameSiteMode.Lax;
        o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        o.ExpireTimeSpan = TimeSpan.FromDays(30);
        o.SlidingExpiration = true;
        // This is an API for a SPA: answer 401/403 instead of redirecting to a login page.
        o.Events.OnRedirectToLogin = ctx => { ctx.Response.StatusCode = 401; return Task.CompletedTask; };
        o.Events.OnRedirectToAccessDenied = ctx => { ctx.Response.StatusCode = 403; return Task.CompletedTask; };
    });
builder.Services.AddAuthorization();

// Brute-force protection on the single-password login.
var loginAttemptsPerMinute = builder.Configuration.GetValue("Auth:LoginAttemptsPerMinute", 5);
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy(AuthEndpoints.LoginRateLimit, ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = loginAttemptsPerMinute, Window = TimeSpan.FromMinutes(1) }));
});

var app = builder.Build();

// Serve the built React frontend from wwwroot (the deploy copies web/dist → wwwroot), so frontend
// and API share one origin. sw.js/index.html/manifest revalidate so updates reach installed PWAs;
// hashed /assets never change and can be cached forever.
var staticFiles = new StaticFileOptions
{
    ContentTypeProvider = new Microsoft.AspNetCore.StaticFiles.FileExtensionContentTypeProvider
    {
        Mappings = { [".webmanifest"] = "application/manifest+json" },
    },
    OnPrepareResponse = ctx =>
    {
        if (ctx.File.Name is "sw.js" or "index.html" or "manifest.webmanifest")
            ctx.Context.Response.Headers.CacheControl = "no-cache";
        else if (ctx.Context.Request.Path.StartsWithSegments("/assets"))
            ctx.Context.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
    },
};
app.UseDefaultFiles();
app.UseStaticFiles(staticFiles);

app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/api/health", (IOptions<TicketmasterOptions> ticketmaster, IConfiguration config) => Results.Ok(new
{
    status = "ok",
    version = typeof(Program).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "dev",
    ticketmaster = ticketmaster.Value.Configured,
    scanKey = !string.IsNullOrEmpty(config["Scan:Key"]),
}));

app.MapAuthEndpoints();
app.MapEventEndpoints();
app.MapProfileEndpoints(usersDir);
app.MapScanEndpoints();
app.MapTasteEndpoints();

// Unknown /api routes are real 404s; everything else is a client-side route → index.html.
app.MapFallback("/api/{**rest}", () => Results.NotFound());
app.MapFallbackToFile("index.html", staticFiles);

app.Run();

public partial class Program;
