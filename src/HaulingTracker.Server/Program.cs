using System.Globalization;
using HaulingTracker.Server;

// The web app: the trackers upload their logs (api/log), the server works out the sessions every two seconds, and the page shows them (/, data.json).
var builder = WebApplication.CreateBuilder(args);
var settings = builder.Configuration.GetSection("HaulingTracker").Get<ServerSettings>() ?? new ServerSettings();
string Full(string path) => Path.IsPathRooted(path) ? path : Path.Combine(builder.Environment.ContentRootPath, path);

// numbers and dates as in the program on Frank's computer (3 614 083); the times are the server's time zone (set it to Europe/Stockholm)
var culture = CultureInfo.GetCultureInfo(settings.Culture);
CultureInfo.DefaultThreadCurrentCulture = culture;
CultureInfo.DefaultThreadCurrentUICulture = culture;

Storage.UseFile(Full(settings.DataFile));
var uploads = new LogUploads(Full(settings.LogDir));
uploads.LoadKept(TimeSpan.FromHours(settings.KeepHours));
var live = new LiveTracker(Storage.Load(), Storage.Save, uploads);

builder.Services.AddSingleton(settings);
builder.Services.AddSingleton(uploads);
builder.Services.AddSingleton(live);
builder.Services.AddSingleton(new TrackerKeys(settings.Trackers));
builder.Services.AddHostedService<RefreshService>();
builder.Services.AddControllersWithViews();

var app = builder.Build();
app.MapControllers();
app.Run();
