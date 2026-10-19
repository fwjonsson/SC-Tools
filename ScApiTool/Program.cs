// ScApiTool: downloads data from the Star Citizen Wiki API (https://api.star-citizen.wiki) and saves it as files.
// Run it with no arguments to download the standard set. Everything lands in a ScApiData folder next to the program,
// as raw .json files plus a summary.txt, and the lot is zipped so you can hand it over in one file.
//   ScApiTool.exe                         the standard set (items is cut at 20 pages)
//   ScApiTool.exe --all                   no page limit at all
//   ScApiTool.exe /api/missions           any other address of the API, saved under custom-...
// Data: api.star-citizen.wiki (unofficial fan site, not affiliated with Cloud Imperium Games). Credit them if you share anything.

using System.IO.Compression;
using System.Text;
using System.Text.Json;

const string BaseUrl = "https://api.star-citizen.wiki";
const int DefaultPageLimit = 60;

var all = args.Contains("--all");
var custom = args.Where(a => !a.StartsWith("--")).ToList();
var stamp = DateTime.Now.ToString("yyyyMMdd-HHmm");
var dataRoot = Path.Combine(AppContext.BaseDirectory, "ScApiData");
var folder = Path.Combine(dataRoot, stamp);
Directory.CreateDirectory(folder);

var http = new HttpClient { Timeout = TimeSpan.FromSeconds(90) };
http.DefaultRequestHeaders.UserAgent.ParseAdd("ScApiTool/1.0 (personal hobby project)");
http.DefaultRequestHeaders.Accept.ParseAdd("application/json");

var summary = new StringBuilder();

Say("Star Citizen Wiki API download, " + DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
Say("Saving to: " + folder);

if (custom.Count > 0)
{
    foreach (var path in custom)
    {
        await Safe("custom-" + Clean(path), path, all ? int.MaxValue : DefaultPageLimit);
    }
}
else
{
    await Safe("game-versions", "/api/game-versions/default", 1);
    await Safe("openapi", "/api/openapi", 1);   // the list of every address the API has, the most useful file of all
    await Safe("locations", "/api/locations", all ? int.MaxValue : DefaultPageLimit);
    await Safe("missions", "/api/missions", all ? int.MaxValue : DefaultPageLimit);
    await Safe("commodities", "/api/commodities", all ? int.MaxValue : DefaultPageLimit);
    await Safe("vehicles", "/api/vehicles", all ? int.MaxValue : DefaultPageLimit);
    await Safe("vehicles-filters", "/api/vehicles/filters", 1);
    await Safe("items", "/api/items", all ? int.MaxValue : 20);   // thousands of records, so only the first pages unless --all
}

Say("");
Say("Done.");
File.WriteAllText(Path.Combine(folder, "summary.txt"), summary.ToString());

var zipPath = Path.Combine(dataRoot, "ScApiData-" + stamp + ".zip");
try
{
    ZipFile.CreateFromDirectory(folder, zipPath);
    Console.WriteLine("Everything is zipped in: " + zipPath);
}
catch (Exception ex)
{
    Console.WriteLine("Could not make the zip (" + ex.Message + "). The files are in: " + folder);
}

Console.WriteLine();
Console.WriteLine("Press Enter to close.");
Console.ReadLine();
return;

// ------------------------------------------------------------------ helpers

void Say(string text)
{
    Console.WriteLine(text);
    summary.AppendLine(text);
}

static string Clean(string text) =>
    new string(text.Select(c => char.IsLetterOrDigit(c) || c == '-' ? c : '_').ToArray()).Trim('_');

static string Cut(string text, int max) => text.Length <= max ? text : text[..max] + "...";

static string Kind(JsonElement e) => e.ValueKind switch
{
    JsonValueKind.Object => "{}",
    JsonValueKind.Array => "[" + e.GetArrayLength() + "]",
    JsonValueKind.String => "text",
    JsonValueKind.Number => "number",
    JsonValueKind.True or JsonValueKind.False => "yes/no",
    _ => "empty",
};

static string Fields(JsonElement record) =>
    record.ValueKind == JsonValueKind.Object
        ? string.Join(", ", record.EnumerateObject().Select(p => p.Name + ":" + Kind(p.Value)))
        : record.ValueKind.ToString();

static string WithPage(string path, int page) => path + (path.Contains('?') ? "&" : "?") + "page=" + page;

async Task Safe(string name, string path, int pageLimit)
{
    try
    {
        await FetchAll(name, path, pageLimit);
    }
    catch (Exception ex)
    {
        Say("  Failed: " + ex.Message);
    }
}

async Task<(int Status, string Body)> Get(string url)
{
    for (var attempt = 1; ; attempt++)
    {
        try
        {
            using var response = await http.GetAsync(url);
            var status = (int)response.StatusCode;
            if (status == 429 && attempt <= 6)
            {
                var wait = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(30);
                if (wait > TimeSpan.FromMinutes(2)) wait = TimeSpan.FromMinutes(2);
                Say("  Too many requests, waiting " + (int)wait.TotalSeconds + " seconds...");
                await Task.Delay(wait);
                continue;
            }
            if (status >= 500 && attempt <= 3)
            {
                await Task.Delay(3000 * attempt);
                continue;
            }
            return (status, await response.Content.ReadAsStringAsync());
        }
        catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException)
        {
            if (attempt >= 3) return (0, ex.Message);
            await Task.Delay(3000 * attempt);
        }
    }
}

/// <summary>Downloads an address and, when the answer is a list with more pages, the next pages too. Every page is saved as it is.</summary>
async Task FetchAll(string name, string path, int pageLimit)
{
    Say("");
    Say("== " + name + "   " + path);

    string? url = path.StartsWith("http") ? path : BaseUrl + path;
    var page = 1;
    var records = 0;
    string? fields = null;
    string? meta1 = null;

    while (url != null && page <= pageLimit)
    {
        var (status, body) = await Get(url);
        if (status != 200)
        {
            Say("  Page " + page + ": HTTP " + status + "  " + Cut(body.Replace('\n', ' '), 200));
            break;
        }
        File.WriteAllText(Path.Combine(folder, Clean(name) + "-" + page.ToString("000") + ".json"), body);

        string? next = null;
        try
        {
            using var doc = JsonDocument.Parse(body);
            var top = doc.RootElement;
            if (top.ValueKind == JsonValueKind.Object)
            {
                if (top.TryGetProperty("data", out var data))
                {
                    if (data.ValueKind == JsonValueKind.Array)
                    {
                        records += data.GetArrayLength();
                        if (fields == null && data.GetArrayLength() > 0) fields = Fields(data[0]);
                    }
                    else
                    {
                        records++;
                        fields ??= Fields(data);
                    }
                }

                if (page == 1 && top.TryGetProperty("meta", out var meta)) meta1 = Cut(meta.GetRawText(), 500);

                if (top.TryGetProperty("links", out var links) && links.ValueKind == JsonValueKind.Object
                    && links.TryGetProperty("next", out var n) && n.ValueKind == JsonValueKind.String)
                {
                    next = n.GetString();
                    if (next != null && next.StartsWith('/')) next = BaseUrl + next;
                }
                else if (top.TryGetProperty("meta", out var m) && m.ValueKind == JsonValueKind.Object
                    && m.TryGetProperty("current_page", out var cur) && cur.TryGetInt32(out var curPage)
                    && m.TryGetProperty("last_page", out var last) && last.TryGetInt32(out var lastPage)
                    && curPage < lastPage)
                {
                    next = BaseUrl + WithPage(path.StartsWith("http") ? path[BaseUrl.Length..] : path, curPage + 1);
                }
            }
        }
        catch (JsonException)
        {
            Say("  Page " + page + " is not JSON, saved as it is.");
        }

        Console.WriteLine("  page " + page + " done, " + records + " records so far");
        url = next;
        page++;
        if (url != null && page <= pageLimit) await Task.Delay(1100);   // be gentle with the site
    }

    Say("  " + records + " records in " + (page - 1) + " page(s).");
    if (fields != null) Say("  A record has: " + fields);
    if (meta1 != null) Say("  Paging info: " + meta1);
    if (url != null && page > pageLimit) Say("  There is more, this stopped at the page limit. Run with --all for everything.");
}
