namespace SCTools.Parties;

/// <summary>What happened in a party (PartyRecord.News).</summary>
public enum PartyNewsKind
{
    /// <summary>He came into the party (the first members when it was made).</summary>
    Joined,

    /// <summary>He went out of it (he left, was kicked, went to another party, or the party was disbanded).</summary>
    Left,

    /// <summary>He became its leader.</summary>
    Leader,

    /// <summary>His game went out (to the menu, quit or crashed): the others' games said "disconnected". He is still in the party until it says he left.</summary>
    Dropped,

    /// <summary>His game came back ("connected") after it went out.</summary>
    Back,
}

/// <summary>One thing that happened in a party, at the time the first log that told it says.</summary>
public sealed record PartyNews(DateTime Time, PartyNewsKind Kind, string Name);
