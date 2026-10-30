namespace SCTools.Parties;

/// <summary>A party in the game now: its leader and everybody in it (a player who plays alone is a party of one).</summary>
public sealed record PartyGroup(string Leader, IReadOnlyList<string> Names)
{
    /// <summary>"zzoega's party", or the name alone for a player who plays alone.</summary>
    public string Title => Names.Count > 1 ? Leader + "'s party" : Leader;
}
