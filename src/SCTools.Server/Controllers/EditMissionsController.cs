using SCTools.Missions;
using SCTools.Server.Accounts;
using SCTools.Server.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace SCTools.Server.Controllers;

/// <summary>
/// Where the admin's fields of the Mission Library are saved (admin only): the aUEC, MG scrip and Council scrip a kind of mission pays, its SCU, another name,
/// the faction (it wins over the Star Citizen Wiki's). The fields are in the Mission Library's own rows for him (/missions, MissionsController) and in the
/// Companion's contracts; each is saved as soon as it changes (autosave.js: in the background, so the page stays where it is). Edit Mission Info, the
/// admin's page of its own at this address, went on 31 Oct 2026 (Frank: the library is where he edits it).
/// </summary>
[Authorize(Roles = Roles.Admin)]
[AutoValidateAntiforgeryToken]
[Route(Path)]
public sealed class EditMissionsController : Controller
{
    public const string Path = "/admin/missions";

    readonly MissionLibrary _library;

    public EditMissionsController(MissionLibrary library) => _library = library;

    /// <summary>A field sent in the background by autosave.js (data-autosave="quiet") has this header: the answer is what to say, not the page again.</summary>
    public const string AutosaveHeader = "X-Autosave";

    const string NotSaved = "Not saved: a reward or SCU is a whole number (no letters).";

    [HttpPost("set")]
    public IActionResult Set(string id, string field, string? value, string? back)
    {
        var said = _library.Set(id ?? "", field ?? "", value, User.Identity?.Name ?? "", DateTime.UtcNow);
        if (Request.Headers.ContainsKey(AutosaveHeader))   // saved in the background (autosave.js): what to say, the page stays as it is
            return Json(new { ok = said != null, said = said ?? NotSaved, version = _library.Version });
        if (said == null) TempData["Message"] = NotSaved;
        var anchor = "#" + MissionLibrary.Anchor(id ?? "");
        return LocalRedirect(back is { Length: > 0 } && back.StartsWith(MissionsController.Path, StringComparison.Ordinal) ? back + anchor : MissionsController.Path + anchor);
    }

    // ------------------------------------------------------------------ a contract screenshot (#437, Frank 31 Oct 2026)

    /// <summary>
    /// What the paste box read from a contract screenshot (screenshot.js: Tesseract in the admin's browser, the text of each field) made into values
    /// (ContractCardReader), with the missions of the library it is most like (the first is the one it is; each with what the library has now) and the known
    /// faction it is. A reward outside the Star Citizen Wiki's range for the mission gets a note, so a misread number is looked at before it is saved.
    /// </summary>
    [HttpPost("screenshot")]
    public IActionResult Screenshot(string? title, string? reward, string? contractor, string? details)
    {
        var card = ContractCardReader.Read(title, reward, contractor, details);
        var entries = _library.Entries();
        var byKey = entries.ToDictionary(e => e.Row.Key, StringComparer.OrdinalIgnoreCase);
        var choices = ContractCardReader.Matches(card.Title, entries.Where(e => !MissionKinds.IsPlaceholder(e.Row.Title)).Select(e => (e.Row.Key, e.Row.Title)));
        var factions = entries.Select(e => e.FactionShown).Where(g => !string.IsNullOrEmpty(g)).Select(g => g!).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var faction = ContractCardReader.Faction(card.Contractor, factions);
        var notes = card.Notes.ToList();
        if (choices.Count > 0 && byKey[choices[0].Key].Wiki is { } w && card.Reward is { } r
            && ((w.RewardMin is > 0 && r < w.RewardMin) || (w.RewardMax is > 0 && r > w.RewardMax)))
            notes.Add("The reward is outside the Star Citizen Wiki's " + w.RewardText + " for this mission: check it.");
        if (card.Contractor != null && faction == null) notes.Add("\"" + card.Contractor + "\" is a faction the library does not know yet: check its spelling.");
        return Json(new
        {
            title = card.Title,
            reward = card.Reward,
            mg = card.Mg,
            council = card.Council,
            faction = faction ?? card.Contractor,
            notes,
            choices = choices.Select(c => byKey[c.Key]).Select(e => new
            {
                key = e.Row.Key,
                name = e.Shown,
                auec = e.Row.Auec,
                mg = e.Row.MgScrip,
                council = e.Row.CouncilScrip,
                faction = e.FactionShown,
                wiki = e.Wiki?.RewardText,
            }),
        });
    }

    /// <summary>
    /// Saves what the admin checked of a screenshot: the reward, the scrip and the faction of this mission (key), the ones filled in (an empty one is left as
    /// it is). Without a key it is a mission nobody played yet (a screenshot of an offer): it is added to the library by its name first.
    /// </summary>
    [HttpPost("screenshot/save")]
    public IActionResult SaveScreenshot(string? key, string? title, string? auec, string? mg, string? council, string? giver)
    {
        var now = DateTime.UtcNow;
        var who = User.Identity?.Name ?? "";
        if (string.IsNullOrWhiteSpace(key))
        {
            key = MissionKinds.TitleKey(title);
            if (key == null) return Json(new { ok = false, said = "Not saved: give the mission its name." });
            _library.Kinds(new[] { new Data.MissionKindRow { Key = key, Title = MissionKinds.KindTitle(title)! } });
        }
        foreach (var (field, value) in new[] { ("auec", auec), ("mg", mg), ("council", council), ("giver", giver) })
        {
            if (string.IsNullOrWhiteSpace(value)) continue;
            if (_library.Set(key, field, value, who, now) == null) return Json(new { ok = false, said = NotSaved });
        }
        return Json(new { ok = true, said = "Saved.", key, anchor = MissionLibrary.Anchor(key), version = _library.Version });
    }
}
