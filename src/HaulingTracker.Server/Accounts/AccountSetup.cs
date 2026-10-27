using Microsoft.AspNetCore.Identity;

namespace HaulingTracker.Server.Accounts;

/// <summary>What the logins need when the server starts: the two roles, and the admin (from the settings) when there is none yet.</summary>
public static class AccountSetup
{
    public static async Task RunAsync(IServiceProvider services, AdminSettings admin, ILogger log)
    {
        var roles = services.GetRequiredService<RoleManager<IdentityRole>>();
        foreach (var role in new[] { Roles.Admin, Roles.Player })
        {
            if (!await roles.RoleExistsAsync(role)) Check(await roles.CreateAsync(new IdentityRole(role)), "the role " + role);
        }

        var users = services.GetRequiredService<UserManager<IdentityUser>>();
        var admins = await users.GetUsersInRoleAsync(Roles.Admin);
        if (admins.Count > 0)
        {
            if (admin.ResetPassword) await ResetAsync(users, admins, admin, log);
            return;
        }
        if (string.IsNullOrWhiteSpace(admin.UserName) || string.IsNullOrEmpty(admin.Password))
        {
            log.LogWarning("There is no admin yet: set HaulingTracker:Admin:UserName and HaulingTracker:Admin:Password (user secrets, or the environment on the server) and start again");
            return;
        }
        var name = admin.UserName.Trim();
        var user = await users.FindByNameAsync(name);
        if (user != null)
        {
            log.LogWarning("There is no admin yet, and the name {Name} is taken by a player: choose another admin name", name);
            return;
        }
        user = new IdentityUser(name);
        Check(await users.CreateAsync(user, admin.Password), "the admin " + name);
        Check(await users.AddToRoleAsync(user, Roles.Admin), "the admin role of " + name);
        log.LogInformation("The admin {Name} is made: log in at /login (the password can be changed there, and removed from the settings)", name);
    }

    /// <summary>A forgotten password: the admin of that name gets the password of the settings again, and is no longer locked out.</summary>
    static async Task ResetAsync(UserManager<IdentityUser> users, IList<IdentityUser> admins, AdminSettings admin, ILogger log)
    {
        var user = admins.FirstOrDefault(u => string.Equals(u.UserName, admin.UserName?.Trim(), StringComparison.OrdinalIgnoreCase));
        if (user == null || string.IsNullOrEmpty(admin.Password))
        {
            log.LogWarning("ResetPassword is on, but there is no admin called {Name}, or no Password in the settings", admin.UserName);
            return;
        }
        var token = await users.GeneratePasswordResetTokenAsync(user);
        Check(await users.ResetPasswordAsync(user, token, admin.Password), "the new password of " + user.UserName);
        await users.SetLockoutEndDateAsync(user, null);
        await users.ResetAccessFailedCountAsync(user);
        log.LogWarning("The password of the admin {Name} is set from the settings again: remove ResetPassword from them now", user.UserName);
    }

    static void Check(IdentityResult result, string what)
    {
        if (!result.Succeeded) throw new InvalidOperationException("Could not make " + what + ": " + string.Join(" ", result.Errors.Select(e => e.Description)));
    }
}
