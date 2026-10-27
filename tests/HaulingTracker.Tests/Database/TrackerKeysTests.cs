using HaulingTracker.Server;

namespace HaulingTracker.Tests.Database;

/// <summary>The keys of the trackers: the database keeps only their hash, and a key says whose log an upload is.</summary>
public class TrackerKeysTests
{
    const string ZzoegaKey = "k3Yf0rZzoega-0123456789abcdef";
    const string BilehKey = "k3Yf0rBileh-0123456789abcdefgh";

    [Fact]
    public void The_keys_of_the_settings_go_into_the_database_as_a_hash_once()
    {
        using var temp = new TempDatabase();
        var settings = new Dictionary<string, string> { [ZzoegaKey] = "zzoega", [BilehKey] = " Bileh123 ", ["short"] = "zzatvo", ["k3Yf0rNobody-0123456789"] = " " };
        Assert.Equal(2, temp.Store.ImportTrackerKeys(settings, DateTime.UtcNow));
        Assert.Equal(0, temp.Store.ImportTrackerKeys(settings, DateTime.UtcNow));   // the next start: already there

        var rows = temp.Store.TrackerKeys();
        Assert.Equal(new[] { "zzoega", "Bileh123" }, rows.Select(r => r.Player));
        Assert.Equal(TrackerKeys.Hash(ZzoegaKey), rows[0].KeyHash);
        Assert.All(rows, r => Assert.Matches("^[0-9A-F]{64}$", r.KeyHash));   // only the hash, not the key itself
    }

    [Fact]
    public void A_key_says_whose_log_it_is()
    {
        using var temp = new TempDatabase();
        temp.Store.ImportTrackerKeys(new Dictionary<string, string> { [ZzoegaKey] = "zzoega", [BilehKey] = "Bileh123" }, DateTime.UtcNow);
        var keys = new TrackerKeys(temp.Store.TrackerKeys());

        Assert.Equal("zzoega", keys.PlayerOf(ZzoegaKey));
        Assert.Equal("Bileh123", keys.PlayerOf(BilehKey));
        Assert.Null(keys.PlayerOf(ZzoegaKey + "x"));
        Assert.Null(keys.PlayerOf(ZzoegaKey.ToUpperInvariant()));
        Assert.Null(keys.PlayerOf("short"));
        Assert.Null(keys.PlayerOf(""));
        Assert.Null(keys.PlayerOf(null));
    }
}
