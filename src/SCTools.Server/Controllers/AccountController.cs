using SCTools.Server.Accounts;
using SCTools.Server.Data;
using SCTools.Server.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using SCTools.Tracking;

namespace SCTools.Server.Controllers;

/// <summary>
/// Logging in and out. The admin logs in with his name and password (and can change the password); a player is logged in by his tracker: "Open my page"
/// asks the server for a link (api/login-link, with the tracker's key) and opens it here, once.
/// </summary>
[AutoValidateAntiforgeryToken]
public sealed class AccountController : Controller
{
    readonly SignInManager<IdentityUser> _signIn;
    readonly UserManager<IdentityUser> _users;
    readonly LoginLinks _links;

    public AccountController(SignInManager<IdentityUser> signIn, UserManager<IdentityUser> users, LoginLinks links)
    {
        _signIn = signIn;
        _users = users;
        _links = links;
    }

    [HttpGet("/login")]
    public IActionResult Login(string? returnUrl = null) => View(new LoginModel { ReturnUrl = returnUrl });

    [HttpPost("/login")]
    public async Task<IActionResult> Login(LoginModel model)
    {
        if (!ModelState.IsValid) return View(model);
        var result = await _signIn.PasswordSignInAsync(model.UserName.Trim(), model.Password, model.Remember, lockoutOnFailure: true);
        if (!result.Succeeded)
        {
            model.Error = result.IsLockedOut ? "Too many wrong passwords: wait ten minutes and try again." : "Wrong name or password.";
            model.Password = "";
            return View(model);
        }
        if (!string.IsNullOrEmpty(model.ReturnUrl) && Url.IsLocalUrl(model.ReturnUrl)) return LocalRedirect(model.ReturnUrl);
        return LocalRedirect("/admin");
    }

    [HttpPost("/logout")]
    public async Task<IActionResult> Logout()
    {
        await _signIn.SignOutAsync();
        return LocalRedirect("/");
    }

    /// <summary>The link a tracker opened: its player is logged in (his login is made the first time), and My page opens.</summary>
    [HttpGet("/login/tracker")]
    public async Task<IActionResult> Tracker(string? token)
    {
        var player = _links.Use(token, DateTime.UtcNow);
        if (player == null) return LinkProblem("This link was used already or is too old. Use Open my page in the tracker again.");

        var user = await _users.FindByNameAsync(player);
        if (user == null)
        {
            user = new IdentityUser(player);
            var made = await _users.CreateAsync(user);
            if (!made.Succeeded) return LinkProblem("The login of " + player + " could not be made: " + string.Join(" ", made.Errors.Select(e => e.Description)));
        }
        if (!await _users.IsInRoleAsync(user, Roles.Player)) await _users.AddToRoleAsync(user, Roles.Player);

        // the admin's own player (Frank's zzoega): his tracker logs him in as the player only, so a tracker key never opens the admin pages
        await HttpContext.SignInAsync(IdentityConstants.ApplicationScheme, await TrackerLogins.PrincipalAsync(_signIn, user), new AuthenticationProperties { IsPersistent = true });
        return LocalRedirect("/me");
    }

    IActionResult LinkProblem(string message) => View("LinkProblem", new LinkProblemModel { Message = message });

    [HttpGet("/login/denied")]
    public IActionResult Denied() => View();

    AccountModel Page(CombineModel combine, NameModel? rename = null, PasswordModel? password = null) =>
        new() { Name = User.Identity?.Name ?? "", Rename = rename ?? new NameModel(), Password = password ?? new PasswordModel(), Combine = combine };

    /// <summary>His other accounts to pick from (every other player the server knows: the tracker keys and the party) and the ones picked.</summary>
    CombineModel CombineOf(DataStore store, LiveTracker live, string? me = null)
    {
        me ??= User.Identity?.Name ?? "";
        var (party, groups) = live.Read(t => (t.State.Data.Party.ToList(), t.State.Data.CombinedAccounts.Select(g => g.ToList()).ToList()));
        var choices = store.TrackerKeys().Select(k => k.Player).Concat(party)
            .Where(n => !string.IsNullOrWhiteSpace(n) && !PlayerNames.SameName(n, me))
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
        var mine = groups.FirstOrDefault(g => g.Any(n => PlayerNames.SameName(n, me)));
        return new CombineModel { Choices = choices, Picked = mine?.Where(n => !PlayerNames.SameName(n, me)).ToList() ?? new List<string>() };
    }

    /// <summary>The admin's Account page: his name, his password, and his other accounts to count with his.</summary>
    [Authorize(Roles = Roles.Admin)]
    [HttpGet("/account")]
    public IActionResult Account([FromServices] DataStore store, [FromServices] LiveTracker live) => View("Account", Page(CombineOf(store, live)));

    /// <summary>The accounts ticked are counted with his in the money tables (Frank, 30 Oct 2026); none ticked: his alone again.</summary>
    [Authorize(Roles = Roles.Admin)]
    [HttpPost("/account/combine")]
    public IActionResult Combine(List<string>? names, [FromServices] DataStore store, [FromServices] LiveTracker live)
    {
        var me = User.Identity?.Name ?? "";
        var choices = CombineOf(store, live).Choices;
        var picked = (names ?? new List<string>()).Where(n => choices.Any(c => PlayerNames.SameName(c, n))).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        live.Edit(t =>
        {
            t.State.Data.CombinedAccounts.RemoveAll(g => g.Any(n => PlayerNames.SameName(n, me)));
            if (picked.Count > 0) t.State.Data.CombinedAccounts.Add(new List<string> { me }.Concat(picked).ToList());
        }, DateTime.UtcNow);
        var combine = CombineOf(store, live);
        combine.Done = true;
        return View("Account", Page(combine));
    }

    [Authorize(Roles = Roles.Admin)]
    [HttpGet("/account/password")]
    public IActionResult Password() => LocalRedirect("/account");

    [Authorize(Roles = Roles.Admin)]
    [HttpPost("/account/name")]
    public async Task<IActionResult> Name([Bind(Prefix = "Rename")] NameModel model, [FromServices] DataStore store, [FromServices] LiveTracker live)
    {
        if (!ModelState.IsValid) return View("Account", Page(CombineOf(store, live), model));
        var name = model.NewName.Trim();
        var user = await _users.GetUserAsync(User);
        if (user == null) return Challenge();
        // the admin's own player (zzoega): the login his tracker made is taken over, so it is one login (the password: admin, the tracker: player only)
        if (await _users.FindByNameAsync(name) is { } other && other.Id != user.Id)
        {
            if (await _users.IsInRoleAsync(other, Roles.Admin))
            {
                ModelState.AddModelError("Rename.NewName", name + " is another admin's name.");
                return View("Account", Page(CombineOf(store, live), model));
            }
            await _users.DeleteAsync(other);
        }
        var isPlayer = store.TrackerKeys().Any(k => PlayerNames.SameName(k.Player, name)) || live.Read(t => t.State.Data.Party.Any(n => PlayerNames.SameName(n, name)));
        if (isPlayer && !await _users.IsInRoleAsync(user, Roles.Player)) await _users.AddToRoleAsync(user, Roles.Player);
        var result = await _users.SetUserNameAsync(user, name);
        if (!result.Succeeded)
        {
            foreach (var e in result.Errors) ModelState.AddModelError("Rename.NewName", e.Description);
            return View("Account", Page(CombineOf(store, live), model));
        }
        await _signIn.RefreshSignInAsync(user);
        ModelState.Clear();
        return View("Account", new AccountModel { Name = name, Rename = new NameModel { Done = true }, Combine = CombineOf(store, live, name) });
    }

    [Authorize(Roles = Roles.Admin)]
    [HttpPost("/account/password")]
    public async Task<IActionResult> Password([Bind(Prefix = "Password")] PasswordModel model, [FromServices] DataStore store, [FromServices] LiveTracker live)
    {
        if (!ModelState.IsValid) return View("Account", Page(CombineOf(store, live), password: model));
        var user = await _users.GetUserAsync(User);
        if (user == null) return Challenge();
        var result = await _users.ChangePasswordAsync(user, model.Current, model.New);
        if (!result.Succeeded)
        {
            foreach (var e in result.Errors) ModelState.AddModelError("Password", e.Description);
            return View("Account", Page(CombineOf(store, live), password: model));
        }
        await _signIn.RefreshSignInAsync(user);
        ModelState.Clear();
        return View("Account", Page(CombineOf(store, live), password: new PasswordModel { Done = true }));
    }
}
