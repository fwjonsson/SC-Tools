using System.Collections;
using System.Text.Json;
using System.Text.Json.Nodes;
using SCTools.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace SCTools.Tests.Database;

/// <summary>A database in a file of its own, deleted afterwards.</summary>
sealed class TempDatabase : IDisposable
{
    public TempDatabase()
    {
        Dir = Path.Combine(Path.GetTempPath(), "ht-db-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Dir);
        Store = DataStore.ForFile(Path.Combine(Dir, "tracker.db"));
        Store.Migrate();
    }

    public string Dir { get; }
    public DataStore Store { get; }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(Dir, true); } catch (IOException) { }
    }
}

/// <summary>The data of the web app in its database: everything comes back out the same as it went in, and only what changed is written.</summary>
public class DataStoreTests
{
    /// <summary>The data as JSON, the members of every object (and so the keys of every dictionary) in the same order, for comparing.</summary>
    static string Canonical(AppData d)
    {
        static JsonNode? Sort(JsonNode? n) => n switch
        {
            JsonObject o => new JsonObject(o.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => KeyValuePair.Create(p.Key, Sort(p.Value)))),
            JsonArray a => new JsonArray(a.Select(Sort).ToArray()),
            null => null,
            _ => n.DeepClone(),
        };
        return Sort(JsonSerializer.SerializeToNode(d))!.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>A value for every field of this type (and of the types in it), each one different: a list or a dictionary gets one entry.</summary>
    sealed class Filler
    {
        int _n;

        public T Make<T>() => (T)Value(typeof(T), typeof(T).Name);

        object Value(Type type, string name)
        {
            var n = ++_n;
            var t = Nullable.GetUnderlyingType(type) ?? type;
            if (t == typeof(string)) return name + n;
            if (t == typeof(int)) return 1000 + n;
            if (t == typeof(bool)) return true;
            if (t == typeof(decimal)) return 1_000_000.5m + n;
            if (t == typeof(double)) return 0.25 + n;
            if (t == typeof(DateTime)) return new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc).AddMinutes(n).AddTicks(n);
            if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(List<>))
            {
                var list = (IList)Activator.CreateInstance(t)!;
                list.Add(Value(t.GetGenericArguments()[0], name));
                return list;
            }
            if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(Dictionary<,>))
            {
                var dict = (IDictionary)Activator.CreateInstance(t)!;
                dict.Add(name + "-key" + n, Value(t.GetGenericArguments()[1], name));
                return dict;
            }
            var o = Activator.CreateInstance(t)!;
            foreach (var p in t.GetProperties().Where(p => p.CanWrite && p.GetIndexParameters().Length == 0)) p.SetValue(o, Value(p.PropertyType, p.Name));
            return o;
        }
    }

    [Fact]
    public void The_database_writes_ahead_so_a_write_does_not_make_the_readers_wait()
    {
        using var temp = new TempDatabase();
        using var db = temp.Store.Open();
        var connection = db.Database.GetDbConnection();
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA journal_mode";
        Assert.Equal("wal", (string)command.ExecuteScalar()!);   // set when the database is made or brought up to date (Migrate)
    }

    [Fact]
    public void Every_field_of_the_data_comes_back_out_of_the_database()
    {
        using var temp = new TempDatabase();
        var data = new Filler().Make<AppData>();
        Assert.NotEmpty(data.Sessions.Values.Single().LockedTrips);   // filled all the way down

        temp.Store.Save(data);
        Assert.Equal(Canonical(data), Canonical(temp.Store.Load()));
    }

    [Fact]
    public void A_session_with_nothing_typed_comes_back_the_same()
    {
        using var temp = new TempDatabase();
        var data = new AppData();
        data.Sessions["manual-20261022230700"] = new SessionInputs();
        temp.Store.Save(data);
        var back = temp.Store.Load();
        Assert.Equal(Canonical(data), Canonical(back));
        Assert.Null(back.Sessions["manual-20261022230700"].Party);
    }

    [Fact]
    public void The_party_keeps_its_order_and_every_member_his_settings()
    {
        using var temp = new TempDatabase();
        var since = new DateTime(2026, 10, 22, 12, 0, 0, DateTimeKind.Utc);
        var data = new AppData
        {
            Party = new List<string> { "zzoega", "zzatvo", "Bileh123" },
            InactiveParty = new List<string> { "Bileh123" },
        };
        data.Ship["zzoega"] = "C2 Hercules Starlifter";
        data.Ship["OldMember"] = "Caterpillar";   // not in the party any more, his ship is kept
        data.WorkerColors["zzatvo"] = unchecked((int)0xFF123456);
        data.ShipCapacity["zzoega"] = 0;
        data.StartPoints["zzatvo"] = new StartPoint { Id = "70120", Since = since };
        temp.Store.Save(data);

        using (var db = temp.Store.Open())
        {
            var members = db.Members.ToDictionary(m => m.Name);
            Assert.Equal(4, members.Count);
            Assert.Equal(0, members["zzoega"].PartyPosition);
            Assert.Equal(2, members["Bileh123"].PartyPosition);
            Assert.True(members["Bileh123"].Inactive);
            Assert.Null(members["OldMember"].PartyPosition);
            Assert.Equal("70120", members["zzatvo"].LocationId);
        }

        var back = temp.Store.Load();
        Assert.Equal(new[] { "zzoega", "zzatvo", "Bileh123" }, back.Party);
        Assert.Equal(new[] { "Bileh123" }, back.InactiveParty);
        Assert.Equal("Caterpillar", back.Ship["OldMember"]);
        Assert.Equal(unchecked((int)0xFF123456), back.WorkerColors["zzatvo"]);
        Assert.Equal(0, back.ShipCapacity["zzoega"]);
        Assert.False(back.ShipCapacity.ContainsKey("zzatvo"));
        Assert.Equal(since, back.StartPoints["zzatvo"].Since);
        Assert.Equal(DateTimeKind.Utc, back.StartPoints["zzatvo"].Since.Kind);   // still UTC: not shown an hour off
    }

    [Fact]
    public void A_change_is_written_and_what_is_gone_is_deleted()
    {
        using var temp = new TempDatabase();
        var data = new AppData();
        data.Transfers.Add(new SavedTransfer { Time = DateTime.UtcNow, From = "zzatvo", To = "zzoega", Amount = 100_000 });
        data.Transfers.Add(new SavedTransfer { Time = DateTime.UtcNow, From = "zzoega", To = "zzatvo", Amount = 50_000 });
        data.Sessions["a"] = new SessionInputs { Reward2 = 1 };
        data.Sessions["b"] = new SessionInputs { Reward2 = 2 };
        data.TypedScu["m|dropoff_1"] = 147;
        temp.Store.Save(data);

        data.Transfers[1].Payment = true;
        data.Transfers.RemoveAt(0);
        data.Sessions.Remove("a");
        data.Sessions["b"].Rows["69936"] = new RowInputs { Notes = "full" };
        data.Sessions["c"] = new SessionInputs();
        data.TypedScu.Clear();
        temp.Store.Save(data);

        var back = temp.Store.Load();
        Assert.Equal(Canonical(data), Canonical(back));
        Assert.True(back.Transfers.Single().Payment);
        Assert.Equal(data.Sessions.Keys, back.Sessions.Keys);   // in the same order
        Assert.Equal("full", back.Sessions["b"].Rows["69936"].Notes);
        using var db = temp.Store.Open();
        Assert.Single(db.Transfers);
        Assert.Empty(db.TypedScu);
    }

    [Fact]
    public void Nothing_is_written_until_the_tracker_changed_something()
    {
        using var temp = new TempDatabase();
        var data = new AppData { Party = new List<string> { "zzoega" } };
        Assert.False(temp.Store.Flush(data));
        Assert.False(temp.Store.HasData());

        temp.Store.Changed(data);
        Assert.True(temp.Store.Flush(data));
        Assert.False(temp.Store.Flush(data));   // written once
        Assert.Equal(new[] { "zzoega" }, temp.Store.Load().Party);
    }

    [Fact]
    public void The_tracker_on_the_server_saves_into_the_database()
    {
        using var temp = new TempDatabase();
        var live = new LiveTracker(Support.PlayedSession.Data(), temp.Store.Changed, new LogUploads());
        live.Tracker.State.Data.LocationNames["70120"] = "Patch";
        live.Tracker.State.Save();
        Assert.True(temp.Store.Flush(live.Tracker.State.Data));
        Assert.Equal("Patch", temp.Store.Load().LocationNames["70120"]);
    }

    [Fact]
    public void The_data_file_goes_into_an_empty_database_once()
    {
        using var temp = new TempDatabase();
        var file = Path.Combine(temp.Dir, "data.json");
        var data = new AppData { Party = new List<string> { "zzoega", "zzatvo" } };
        data.Payouts.Add(new PayoutRecord { MissionId = "m1", Contract = "Potassium, 3 stops", Payout = 2_113_750, Members = { "zzoega", "zzatvo" }, Each = 1_056_875 });
        File.WriteAllText(file, JsonSerializer.Serialize(data));

        var said = new List<string>();
        var loaded = temp.Store.LoadOrImport(file, said.Add);
        Assert.Equal(Canonical(data), Canonical(loaded));
        Assert.Contains("2 in the party", said.Single());

        // the next start reads the database: the data file is not read again
        File.WriteAllText(file, JsonSerializer.Serialize(new AppData { Party = new List<string> { "someone" } }));
        said.Clear();
        Assert.Equal(new[] { "zzoega", "zzatvo" }, temp.Store.LoadOrImport(file, said.Add).Party);
        Assert.Empty(said);
    }

    [Fact]
    public void Without_a_data_file_the_database_starts_empty_and_a_broken_one_stops_the_server()
    {
        using (var temp = new TempDatabase())
        {
            var data = temp.Store.LoadOrImport(Path.Combine(temp.Dir, "none.json"), _ => { });
            Assert.Equal(new AppData().Party, data.Party);   // the built-in party
            Assert.True(temp.Store.HasData());
        }
        using (var temp = new TempDatabase())
        {
            var file = Path.Combine(temp.Dir, "data.json");
            File.WriteAllText(file, "{ broken");
            Assert.ThrowsAny<JsonException>(() => temp.Store.LoadOrImport(file, _ => { }));
            Assert.False(temp.Store.HasData());   // still empty: the next start tries again
        }
    }

    [Fact]
    public void A_copy_of_the_database_is_made_once_a_day()
    {
        using var temp = new TempDatabase();
        temp.Store.Save(new AppData { Party = new List<string> { "zzoega" } });
        var dir = Path.Combine(temp.Dir, "backups");
        Assert.True(temp.Store.BackupDaily(dir, new DateTime(2026, 10, 27, 20, 0, 0)));
        Assert.False(temp.Store.BackupDaily(dir, new DateTime(2026, 10, 27, 23, 0, 0)));
        Assert.True(temp.Store.BackupDaily(dir, new DateTime(2026, 10, 28, 0, 5, 0)));
        Assert.Equal(new[] { "tracker-2026-10-27.db", "tracker-2026-10-28.db" }, Directory.GetFiles(dir).Select(f => Path.GetFileName(f)).Order());

        // the copy is a database with the data in it
        var copy = DataStore.ForFile(Path.Combine(dir, "tracker-2026-10-27.db"));
        Assert.Equal(new[] { "zzoega" }, copy.Load().Party);
    }

    [Fact]
    public void The_migrations_have_every_table_and_column()
    {
        using var temp = new TempDatabase();
        using var db = temp.Store.Open();
        Assert.False(db.Database.HasPendingModelChanges(), "TrackerDb changed without a migration: see Data/Migrations in the README");
        Assert.Empty(db.Database.GetPendingMigrations());
    }
}
