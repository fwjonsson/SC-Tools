using System.Globalization;
using SCTools.Server;
using SCTools.Server.Accounts;
using SCTools.Server.Data;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

// The web app: the trackers upload their logs (api/log), the server works out the sessions every two seconds, and the pages show them (/hauling, data.json).
// The data (the party, the sessions, the payouts, the debts...) is in the database; the first time the program's data file goes into it. The admin logs in
// with a password, a player with his tracker; the pages to change things are MVC (Controllers, Models, Views).
var builder = WebApplication.CreateBuilder(args);

// the settings ("SCTools" in appsettings.json, the user secrets or the environment: SCTools__Database...), read when the server is built. The names from
// before the rename (HaulingTracker__Database...) are read first, so a server whose settings still have them works the same.
builder.Services.Configure<ServerSettings>(builder.Configuration.GetSection("HaulingTracker"));
builder.Services.Configure<ServerSettings>(builder.Configuration.GetSection("SCTools"));
builder.Services.AddSingleton(sp => sp.GetRequiredService<IOptions<ServerSettings>>().Value);
builder.Services.AddSingleton<ServerPaths>();

builder.Services.AddSingleton(sp => DataStore.ForFile(sp.GetRequiredService<ServerPaths>().Database));
builder.Services.AddSingleton(sp =>
{
    var uploads = new LogUploads(sp.GetRequiredService<ServerPaths>().LogDir);
    uploads.LoadKept(TimeSpan.FromHours(sp.GetRequiredService<ServerSettings>().KeepHours));
    return uploads;
});
builder.Services.AddSingleton(sp =>
{
    var store = sp.GetRequiredService<DataStore>();
    var log = sp.GetRequiredService<ILogger<DataStore>>();
    var data = store.LoadOrImport(sp.GetRequiredService<ServerPaths>().DataFile, m => log.LogInformation("{Message}", m));
    var settings = sp.GetRequiredService<ServerSettings>();
    return new LiveTracker(data, store.Changed, sp.GetRequiredService<LogUploads>(), new WebSnapshotOptions { DebtsPay = "/hauling/debts/payment", RunsLost = "/hauling/runs/lost", TripScu = "/hauling/trips/scu", TripEnd = "/hauling/trips/end" },
        followParty: settings.FollowParty, followSessions: settings.FollowSessions);
});
builder.Services.AddSingleton(sp =>
{
    var store = sp.GetRequiredService<DataStore>();
    var added = store.ImportTrackerKeys(sp.GetRequiredService<ServerSettings>().Trackers, DateTime.UtcNow);
    if (added > 0) sp.GetRequiredService<ILogger<TrackerKeys>>().LogInformation("{Count} tracker keys from the settings are now in the database", added);
    return new TrackerKeys(store.TrackerKeys());
});
builder.Services.AddSingleton<LoginLinks>();
builder.Services.AddSingleton(_ => new TrackerVersions());
builder.Services.AddSingleton<ToolAccess>();
builder.Services.AddSingleton<GauntletService>();
builder.Services.AddSingleton<ScripValues>();
builder.Services.AddSingleton<Playtime>();
builder.Services.AddSingleton<FleetService>();
builder.Services.AddSingleton<PartyService>();
builder.Services.AddSingleton<MapPlaces>();
builder.Services.AddSingleton<MapService>();
builder.Services.AddSingleton<PlannerPositions>();
builder.Services.AddSingleton<HistoryStore>();
builder.Services.AddSingleton<MissionLibrary>();
builder.Services.AddSingleton<WikiMissions>();
builder.Services.AddHostedService<MissionLibraryService>();
builder.Services.AddHostedService<RefreshService>();

// the logins: ASP.NET Core Identity, its users and roles in the same database
builder.Services.AddDbContext<TrackerDb>((sp, o) => o.UseSqlite(DataStore.ConnectionString(sp.GetRequiredService<ServerPaths>().Database)));
builder.Services.AddIdentity<IdentityUser, IdentityRole>(o =>
    {
        o.Password.RequiredLength = 12;   // a long password, no rules about the kinds of characters
        o.Password.RequireDigit = false;
        o.Password.RequireLowercase = false;
        o.Password.RequireUppercase = false;
        o.Password.RequireNonAlphanumeric = false;
        o.Lockout.MaxFailedAccessAttempts = 5;
        o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(10);
    })
    .AddEntityFrameworkStores<TrackerDb>()
    .AddDefaultTokenProviders();
// the keys of the login cookies are kept with the database, so a restart of the server (an update) does not log everybody out
builder.Services.AddDataProtection().SetApplicationName("SCTools");
builder.Services.AddOptions<KeyManagementOptions>().Configure<ServerPaths, ILoggerFactory>((o, paths, logs) =>
    o.XmlRepository = new FileSystemXmlRepository(new DirectoryInfo(Path.Combine(Path.GetDirectoryName(paths.Database)!, "keys")), logs));
builder.Services.ConfigureApplicationCookie(o =>
{
    o.Cookie.Name = "SCTools";
    o.LoginPath = "/login";
    o.LogoutPath = "/logout";
    o.AccessDeniedPath = "/login/denied";
    o.ExpireTimeSpan = TimeSpan.FromDays(30);
    o.SlidingExpiration = true;
});
// a tracker key taken back logs that player's browser out within a minute
builder.Services.Configure<SecurityStampValidatorOptions>(o =>
{
    o.ValidationInterval = TimeSpan.FromMinutes(1);
    o.OnRefreshingPrincipal = TrackerLogins.KeepAsync;   // a login made by a tracker keeps the player's rights only
});
builder.Services.AddControllersWithViews();

var app = builder.Build();

// numbers and dates as in the program on Frank's computer (3 614 083); the times are the server's time zone (Europe/Stockholm)
var settings = app.Services.GetRequiredService<ServerSettings>();
if (!string.IsNullOrEmpty(settings.Culture))
{
    var culture = CultureInfo.GetCultureInfo(settings.Culture);
    CultureInfo.DefaultThreadCurrentCulture = culture;
    CultureInfo.DefaultThreadCurrentUICulture = culture;
}

// the database is read (or made, and the data file imported) now, so a problem stops the server at once; then the roles and the admin
app.Services.GetRequiredService<LiveTracker>();
var mapPlaces = app.Services.GetRequiredService<MapPlaces>();
SCTools.GameData.StarRoutes.AtlasSource = () => mapPlaces.Atlas;   // the planner's routes know the places the admin added or renamed
app.Services.GetRequiredService<TrackerKeys>();
using (var scope = app.Services.CreateScope())
{
    await AccountSetup.RunAsync(scope.ServiceProvider, settings.Admin, app.Logger);
}

// behind the Cloudflare tunnel the page is https: the tunnel says so, and the login cookie is then only sent over https
app.UseForwardedHeaders(new ForwardedHeadersOptions { ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto });
app.UseOldAddresses(settings);   // haulingtracker and sct.fwjonsson.dev: the same page at sc-tools.fwjonsson.dev (the trackers' uploads are still taken)
app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.Run();

/// <summary>The server (its tests start it with WebApplicationFactory).</summary>
public partial class Program
{
}
