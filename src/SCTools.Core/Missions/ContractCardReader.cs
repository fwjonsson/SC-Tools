using System.Globalization;
using System.Text.RegularExpressions;

namespace SCTools.Missions;

/// <summary>
/// Turns what the text recognition read from a contract screenshot (Tesseract in the admin's browser, field by field: the title, the Reward, "Contracted
/// By", the description) into a ContractCard, and finds the faction it is in the Mission Library (Frank, 31 Oct 2026: a free text reader, no AI; the
/// mission is the one whose 📷 button the admin pressed, never one guessed by its name). The reward is the part that must be right: it comes read with digits only (the currency sign before it was cut off: read as a digit it made
/// 35,000 into 135,000), and a number whose commas are not every three digits, or under 1,000, counts as not read.
/// </summary>
public static class ContractCardReader
{
    static readonly Regex Scrip = new(@"(?<n>\d[\d,.]*)\s+(?<k>MG|Council)\s+Scrip", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    static readonly Regex Reputation = new(@"Reputation\s+Awarded\s*:?\s*(?<n>\d[\d,.]*)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    static readonly Regex RepMark = new(@"\[\s*(?<n>\d[\d,.]*)\s*Rep\s*\]", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    static readonly Regex Grouped = new(@"^\d{1,3}([,.]\d{3})*$|^\d+$", RegexOptions.Compiled);
    static readonly Regex Marks = new(@"\[[^\]]*\]?|\*", RegexOptions.Compiled);

    /// <summary>The least a contract pays: a reward read under it is a stray mark (Frank's screenshot with a lamp on the reward read "2").</summary>
    public const long LeastReward = 1_000;

    public static ContractCard Read(string? title, string? reward, string? contractor, string? details)
    {
        var notes = new List<string>();
        var auec = Reward(reward);
        if (auec == null && !string.IsNullOrWhiteSpace(reward)) notes.Add("The reward could not be read for sure (\"" + reward.Trim() + "\"): type it in.");
        else if (auec == null) notes.Add("No reward was read: type it in.");
        long? mg = null, council = null;
        foreach (Match m in Scrip.Matches(OneLine(details) ?? ""))
        {
            if (Reward(m.Groups["n"].Value, 1) is not { } n) continue;
            if (m.Groups["k"].Value.Equals("MG", StringComparison.OrdinalIgnoreCase)) mg ??= n;
            else council ??= n;
        }
        // the reputation it gives: "Reputation Awarded: 250" of the description, else the "[250 Rep]" of its name
        var xp = Reputation.Match(OneLine(details) ?? "") is { Success: true } rep ? Reward(rep.Groups["n"].Value, 1)
            : RepMark.Match(OneLine(title) ?? "") is { Success: true } mark ? Reward(mark.Groups["n"].Value, 1) : null;
        return new ContractCard(Title(title), auec, mg, council, Contractor(contractor), notes, xp);
    }

    /// <summary>A text on one line: its lines and runs of spaces as one space (a title or a description goes over lines on the card).</summary>
    static string? OneLine(string? text) =>
        string.IsNullOrWhiteSpace(text) ? null : string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    /// <summary>The contract's name: on one line; in a bounty's name a lone "I" before the risk is its "|".</summary>
    public static string? Title(string? read)
    {
        var t = OneLine(read)?.Replace(" / ", " ", StringComparison.Ordinal);
        if (t == null) return null;
        return t.Contains("Bounty:", StringComparison.OrdinalIgnoreCase) ? Regex.Replace(t, @"\s[Il1]\s", " | ") : t;
    }

    /// <summary>A number read with digits only: its commas (or dots) every three digits, else not read; under least, not read.</summary>
    public static long? Reward(string? read, long least = LeastReward)
    {
        var t = (read ?? "").Trim().Replace(" ", "");
        if (t.Length == 0 || !Grouped.IsMatch(t)) return null;
        return long.TryParse(t.Replace(",", "").Replace(".", ""), NumberStyles.None, CultureInfo.InvariantCulture, out var v) && v >= least ? v : null;
    }

    /// <summary>"Contracted By": on one line, without the stray marks a reflection leaves before it (a lone lowercase letter or two).</summary>
    public static string? Contractor(string? read)
    {
        var words = (OneLine(read) ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        while (words.Count > 1 && words[0].Length <= 2 && words[0].All(char.IsLower)) words.RemoveAt(0);
        return words.Count > 0 ? string.Join(' ', words) : null;
    }

    /// <summary>A name to compare by: the kind it is (a bounty without its target), without the marks a string mod adds ("[250 Rep] [BP]*"), in small letters.</summary>
    public static string Plain(string? name)
    {
        var t = Marks.Replace(MissionKinds.KindTitle(name) ?? "", " ");
        return string.Join(' ', t.ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>How alike two names are, from 0 to 1: one minus the letters to change (Levenshtein) over the longer one.</summary>
    public static double Alike(string a, string b)
    {
        if (a.Length == 0 && b.Length == 0) return 1;
        var prev = new int[b.Length + 1];
        var cur = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++) prev[j] = j;
        for (var i = 1; i <= a.Length; i++)
        {
            cur[0] = i;
            for (var j = 1; j <= b.Length; j++)
                cur[j] = Math.Min(Math.Min(cur[j - 1] + 1, prev[j] + 1), prev[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            (prev, cur) = (cur, prev);
        }
        return 1 - (double)prev[b.Length] / Math.Max(a.Length, b.Length);
    }

    /// <summary>The known faction most like the one read (at least 0.75 alike, in small letters), else null.</summary>
    public static string? Faction(string? read, IEnumerable<string> factions)
    {
        if (string.IsNullOrWhiteSpace(read)) return null;
        var r = read.Trim().ToLowerInvariant();
        return factions.Select(f => (f, Alike: Alike(r, f.ToLowerInvariant()))).Where(x => x.Alike >= 0.75)
            .OrderByDescending(x => x.Alike).Select(x => x.f).FirstOrDefault();
    }
}
