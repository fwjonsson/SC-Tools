using System.ComponentModel.DataAnnotations;

namespace HaulingTracker.Server.Models;

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
