using System.ComponentModel.DataAnnotations;

namespace SCTools.Server.Models;

/// <summary>The tracker keys page of the admin: every key (only who it is for and when it was made: the key itself is not kept), and the form for a new one.</summary>
public sealed class KeysModel
{
    public List<KeyLine> Keys { get; set; } = new();

    /// <summary>The party, to pick a name from.</summary>
    public List<string> Party { get; set; } = new();

    public NewKeyForm New { get; set; } = new();
}

/// <summary>A key: for whom, when it was made, and his tracker (TrackerVersions.Describe; Newest: its colour).</summary>
public sealed record KeyLine(int Id, string Player, string Created, string Tracker, bool? Newest);

/// <summary>A new key for a player.</summary>
public sealed class NewKeyForm
{
    /// <summary>His name in the game: the name in his Game.log. Letters, digits and - _ . only (it is also the name of his login).</summary>
    [Required(ErrorMessage = "Type the player's name in the game.")]
    [RegularExpression("^[A-Za-z0-9_.-]{1,40}$", ErrorMessage = "A name in the game has only letters, digits and - _ .")]
    [Display(Name = "Player (his name in the game)")]
    public string Player { get; set; } = "";
}

/// <summary>A key that was just made: shown this once.</summary>
public sealed class KeyMadeModel
{
    public string Player { get; set; } = "";
    public string Key { get; set; } = "";
    public string ServerUrl { get; set; } = "";
}

/// <summary>The Tracker page: for a logged in player, a line about his tracker (TrackerVersions.Describe) and whether it is the newest.</summary>
public sealed record TrackerPage(string? Player, string? Line, bool? Newest);
