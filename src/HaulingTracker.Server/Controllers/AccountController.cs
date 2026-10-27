using HaulingTracker.Server.Accounts;
using HaulingTracker.Server.Data;
using HaulingTracker.Server.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace HaulingTracker.Server.Controllers;

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
        else if (await _users.IsInRoleAsync(user, Roles.Admin))
        {
            return LinkProblem(player + " is the name of the admin's login, so a tracker cannot log in with it. Log in with the password instead.");
        }
        if (!await _users.IsInRoleAsync(user, Roles.Player)) await _users.AddToRoleAsync(user, Roles.Player);

        await _signIn.SignInAsync(user, isPersistent: true);
        return LocalRedirect("/me");
    }

    IActionResult LinkProblem(string message) => View("LinkProblem", new LinkProblemModel { Message = message });

    [HttpGet("/login/denied")]
    public IActionResult Denied() => View();

    AccountModel Page(NameModel? rename = null, PasswordModel? password = null) =>
        new() { Name = User.Identity?.Name ?? "", Rename = rename ?? new NameModel(), Password = password ?? new PasswordModel() };

    /// <summary>The admin's Account page: his name and his password.</summary>
    [Authorize(Roles = Roles.Admin)]
    [HttpGet("/account")]
    public IActionResult Account() => View("Account", Page());

    [Authorize(Roles = Roles.Admin)]
    [HttpGet("/account/password")]
    public IActionResult Password() => LocalRedirect("/account");

    [Authorize(Roles = Roles.Admin)]
    [HttpPost("/account/name")]
    public async Task<IActionResult> Name([Bind(Prefix = "Rename")] NameModel model, [FromServices] DataStore store, [FromServices] LiveTracker live)
    {
        if (!ModelState.IsValid) return View("Account", Page(model));
        var name = model.NewName.Trim();
        // a name of a player (in the party, or with a tracker key) stays his: his tracker logs in with it
        if (store.TrackerKeys().Any(k => PlayerNames.SameName(k.Player, name)) || live.Read(t => t.State.Data.Party.Any(n => PlayerNames.SameName(n, name))))
        {
            ModelState.AddModelError("Rename.NewName", name + " is a player's name: his tracker logs in with it. Choose another one.");
            return View("Account", Page(model));
        }
        var user = await _users.GetUserAsync(User);
        if (user == null) return Challenge();
        var result = await _users.SetUserNameAsync(user, name);
        if (!result.Succeeded)
        {
            foreach (var e in result.Errors) ModelState.AddModelError("Rename.NewName", e.Description);
            return View("Account", Page(model));
        }
        await _signIn.RefreshSignInAsync(user);
        ModelState.Clear();
        return View("Account", new AccountModel { Name = name, Rename = new NameModel { Done = true } });
    }

    [Authorize(Roles = Roles.Admin)]
    [HttpPost("/account/password")]
    public async Task<IActionResult> Password([Bind(Prefix = "Password")] PasswordModel model)
    {
        if (!ModelState.IsValid) return View("Account", Page(password: model));
        var user = await _users.GetUserAsync(User);
        if (user == null) return Challenge();
        var result = await _users.ChangePasswordAsync(user, model.Current, model.New);
        if (!result.Succeeded)
        {
            foreach (var e in result.Errors) ModelState.AddModelError("Password", e.Description);
            return View("Account", Page(password: model));
        }
        await _signIn.RefreshSignInAsync(user);
        ModelState.Clear();
        return View("Account", Page(password: new PasswordModel { Done = true }));
    }
}
