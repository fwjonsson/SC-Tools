using System.Globalization;
using HaulingTracker.Server;
using HaulingTracker.Server.Data;

// The web app: the trackers upload their logs (api/log), the server works out the sessions every two seconds, and the page shows them (/, data.json).
// The data (the party, the sessions, the payouts, the debts...) is in the database; the first time the program's data file goes into it.
var builder = WebApplication.CreateBuilder(args);
var settings = builder.Configuration.GetSection("HaulingTracker").Get<ServerSettings>() ?? new ServerSettings();
string Full(string path) => Path.IsPathRooted(path) ? path : Path.Combine(builder.Environment.ContentRootPath, path);

// numbers and dates as in the program on Frank's computer (3 614 083); the times are the server's time zone (set it to Europe/Stockholm)
var culture = CultureInfo.GetCultureInfo(settings.Culture);
CultureInfo.DefaultThreadCurrentCulture = culture;
CultureInfo.DefaultThreadCurrentUICulture = culture;

var store = DataStore.ForFile(Full(settings.Database));
var uploads = new LogUploads(Full(settings.LogDir));
uploads.LoadKept(TimeSpan.FromHours(settings.KeepHours));

builder.Services.AddSingleton(settings);
builder.Services.AddSingleton(store);
builder.Services.AddSingleton(uploads);
builder.Services.AddSingleton(sp =>
{
    var log = sp.GetRequiredService<ILogger<DataStore>>();
    var data = store.LoadOrImport(Full(settings.DataFile), m => log.LogInformation("{Message}", m));
    return new LiveTracker(data, store.Changed, uploads);
});
builder.Services.AddSingleton(sp =>
{
    var added = store.ImportTrackerKeys(settings.Trackers, DateTime.UtcNow);
    if (added > 0) sp.GetRequiredService<ILogger<TrackerKeys>>().LogInformation("{Count} tracker keys from the settings are now in the database", added);
    return new TrackerKeys(store.TrackerKeys());
});
builder.Services.AddHostedService<RefreshService>();
builder.Services.AddControllersWithViews();

var app = builder.Build();

// the database is read (or made, and the data file imported) now, so a problem stops the server at once
app.Services.GetRequiredService<LiveTracker>();
app.Services.GetRequiredService<TrackerKeys>();

app.MapControllers();
app.Run();
