using System.ComponentModel.DataAnnotations;

namespace SCTools.Server.Models;

/// <summary>The login form of the admin.</summary>
public sealed class LoginModel
{
    [Required(ErrorMessage = "Type your name.")]
    [Display(Name = "Name")]
    public string UserName { get; set; } = "";

    [Required(ErrorMessage = "Type your password.")]
    [DataType(DataType.Password)]
    public string Password { get; set; } = "";

    [Display(Name = "Stay logged in on this computer")]
    public bool Remember { get; set; } = true;

    /// <summary>The page to go back to after the login.</summary>
    public string? ReturnUrl { get; set; }

    public string? Error { get; set; }
}

/// <summary>The admin's Account page: his name, the forms to change it and his password, and his other accounts to count with his.</summary>
public sealed class AccountModel
{
    public string Name { get; set; } = "";
    public NameModel Rename { get; set; } = new();
    public PasswordModel Password { get; set; } = new();
    public CombineModel Combine { get; set; } = new();
}

/// <summary>
/// The admin's other accounts (Frank, 30 Oct 2026: zzatvo and zzoii with zzoega): the money tables get a row that adds them up with his, for a look at what
/// he earns in all. Only there: the debts stay each account's own, since the work is done on each (AppData.CombinedAccounts).
/// </summary>
public sealed class CombineModel
{
    public List<string> Choices { get; set; } = new();   // every other player known (the tracker keys, the party)
    public List<string> Picked { get; set; } = new();    // the ones counted with his
    public bool Done { get; set; }
}

/// <summary>The admin changes his name (the one he logs in with).</summary>
public sealed class NameModel
{
    [Required(ErrorMessage = "Type the new name.")]
    [RegularExpression("^[A-Za-z0-9_.@+-]{1,40}$", ErrorMessage = "A name has only letters, digits and - _ . @ +")]
    [Display(Name = "New name")]
    public string NewName { get; set; } = "";

    public bool Done { get; set; }
}

/// <summary>The admin changes his password.</summary>
public sealed class PasswordModel
{
    [Required(ErrorMessage = "Type your password now.")]
    [DataType(DataType.Password)]
    [Display(Name = "Password now")]
    public string Current { get; set; } = "";

    [Required(ErrorMessage = "Type the new password.")]
    [DataType(DataType.Password)]
    [Display(Name = "New password")]
    [MinLength(12, ErrorMessage = "The new password needs at least 12 characters.")]
    public string New { get; set; } = "";

    [DataType(DataType.Password)]
    [Display(Name = "New password again")]
    [Compare(nameof(New), ErrorMessage = "The two new passwords are not the same.")]
    public string? Again { get; set; }

    public bool Done { get; set; }
}

/// <summary>Something went wrong with a login link of a tracker.</summary>
public sealed class LinkProblemModel
{
    public string Message { get; set; } = "";
}
