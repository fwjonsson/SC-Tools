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
        if ((await users.GetUsersInRoleAsync(Roles.Admin)).Count > 0) return;
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

    static void Check(IdentityResult result, string what)
    {
        if (!result.Succeeded) throw new InvalidOperationException("Could not make " + what + ": " + string.Join(" ", result.Errors.Select(e => e.Description)));
    }
}
