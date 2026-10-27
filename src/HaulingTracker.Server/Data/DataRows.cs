namespace HaulingTracker.Server.Data;

/// <summary>
/// All the data as rows of the tables, and back: what the core works with (AppData, the same as the data file) is turned into the rows of the database, and
/// the rows into AppData again. Nothing is lost on the way (the tests fill every field of AppData and check that it comes back).
/// </summary>
public sealed class DataRows
{
    public SettingsRow Settings { get; set; } = new();
    public List<MemberRow> Members { get; set; } = new();
    public List<PartyChangeRow> PartyChanges { get; set; } = new();
    public List<ManualSessionRow> ManualSessions { get; set; } = new();
    public List<SessionRow> Sessions { get; set; } = new();
    public List<TransferRow> Transfers { get; set; } = new();
    public List<PayoutRow> Payouts { get; set; } = new();
    public List<MissionHolderRow> MissionHolders { get; set; } = new();
    public List<PayoutOverrideRow> PayoutOverrides { get; set; } = new();
    public List<DroppedMissionRow> DroppedMissions { get; set; } = new();
    public List<TypedScuRow> TypedScu { get; set; } = new();
    public List<PairAdjustRow> PairAdjust { get; set; } = new();
    public List<LocationNameRow> LocationNames { get; set; } = new();

    /// <summary>The rows of this data.</summary>
    public static DataRows From(AppData d)
    {
        var rows = new DataRows
        {
            Settings = new SettingsRow
            {
                PartyTrackingSince = d.PartyTrackingSince,
                PlanCreditSince = d.PlanCreditSince,
                GameLogPath = d.GameLogPath,
                DarkMode = d.DarkMode,
                Channel = d.Channel,
                WebView = d.WebView,
                WebViewPort = d.WebViewPort,
                WatchPaths = d.WatchPaths,
                DisabledWatchPaths = d.DisabledWatchPaths,
            },
        };

        // the members: everybody in the party list, then everybody else something is set for
        var members = new Dictionary<string, MemberRow>(StringComparer.Ordinal);
        MemberRow Member(string name)
        {
            if (!members.TryGetValue(name, out var m))
            {
                m = new MemberRow { Name = name };
                members[name] = m;
                rows.Members.Add(m);
            }
            return m;
        }
        for (var i = 0; i < d.Party.Count; i++)
        {
            var m = Member(d.Party[i]);
            m.PartyPosition ??= i;
        }
        foreach (var name in d.InactiveParty) Member(name).Inactive = true;
        foreach (var (name, ship) in d.Ship) Member(name).Ship = ship;
        foreach (var (name, drive) in d.QuantumDrive) Member(name).QuantumDrive = drive;
        foreach (var (name, color) in d.WorkerColors) Member(name).Color = color;
        foreach (var (name, batches) in d.ShipCapacity) Member(name).ShipCapacity = batches;
        foreach (var (name, at) in d.StartPoints)
        {
            var m = Member(name);
            m.LocationId = at.Id;
            m.LocationSince = at.Since;
        }

        rows.PartyChanges = d.PartyChanges.Select((c, i) => new PartyChangeRow { Position = i, Name = c.Name, TimeUtc = c.TimeUtc, Active = c.Active }).ToList();
        rows.ManualSessions = d.ManualSessions.Select((s, i) => new ManualSessionRow { Position = i, StartUtc = s.StartUtc, StopUtc = s.StopUtc }).ToList();
        rows.Sessions = d.Sessions.Select((kv, i) => SessionRowOf(kv.Key, i, kv.Value)).ToList();
        rows.Transfers = d.Transfers.Select((t, i) => new TransferRow { Position = i, Time = t.Time, From = t.From, To = t.To, Amount = t.Amount, Payment = t.Payment }).ToList();
        rows.Payouts = d.Payouts.Select((p, i) => new PayoutRow
        {
            Position = i, MissionId = p.MissionId, Time = p.Time, Contract = p.Contract, Payout = p.Payout, Members = p.Members, Each = p.Each,
        }).ToList();
        rows.MissionHolders = d.MissionHolders
            .SelectMany(m => m.Value.Select(h => new MissionHolderRow { MissionId = m.Key, Member = h.Key, Has = h.Value }))
            .ToList();
        rows.PayoutOverrides = d.PayoutOverrides.Select(kv => new PayoutOverrideRow { MissionId = kv.Key, Payout = kv.Value }).ToList();
        rows.DroppedMissions = d.DroppedMissions.Select(kv => new DroppedMissionRow { MissionId = kv.Key, Time = kv.Value }).ToList();
        rows.TypedScu = d.TypedScu.Select(kv => new TypedScuRow { Objective = kv.Key, Scu = kv.Value }).ToList();
        rows.PairAdjust = d.PairAdjust.Select(kv => new PairAdjustRow { Pair = kv.Key, Amount = kv.Value }).ToList();
        rows.LocationNames = d.LocationNames.Select(kv => new LocationNameRow { LocationId = kv.Key, Name = kv.Value }).ToList();
        return rows;
    }

    static SessionRow SessionRowOf(string key, int position, SessionInputs s) => new()
    {
        Key = key,
        Position = position,
        Start = s.Start,
        End = s.End,
        StartUtc = s.StartUtc,
        EndUtc = s.EndUtc,
        MissionCount = s.MissionCount,
        Revenue = s.Revenue,
        Reward2 = s.Reward2,
        Reward3 = s.Reward3,
        Reward4 = s.Reward4,
        WasteReward2 = s.WasteReward2,
        WasteReward3 = s.WasteReward3,
        WasteReward4 = s.WasteReward4,
        DirectReward = s.DirectReward,
        NextStopsLocked = s.NextStopsLocked,
        WorkersResetAt = s.WorkersResetAt,
        Rewards = s.Rewards,
        LostRuns = s.LostRuns,
        LostCargo = s.LostCargo,
        ManualEnds = s.ManualEnds,
        Rows = s.Rows,
        PickupClaims = s.PickupClaims,
        LockedTrips = s.LockedTrips,
        PastWorkers = s.PastWorkers,
        Carried = s.Carried,
        RunValues = s.RunValues,
        FailedValues = s.FailedValues,
        MissionIds = s.MissionIds,
        Party = s.Party,
        Stats = s.Stats,
        Received = s.Received,
        Members = s.Members,
    };

    static SessionInputs SessionOf(SessionRow r) => new()
    {
        Start = r.Start,
        End = r.End,
        StartUtc = r.StartUtc,
        EndUtc = r.EndUtc,
        MissionCount = r.MissionCount,
        Revenue = r.Revenue,
        Reward2 = r.Reward2,
        Reward3 = r.Reward3,
        Reward4 = r.Reward4,
        WasteReward2 = r.WasteReward2,
        WasteReward3 = r.WasteReward3,
        WasteReward4 = r.WasteReward4,
        DirectReward = r.DirectReward,
        NextStopsLocked = r.NextStopsLocked,
        WorkersResetAt = r.WorkersResetAt,
        Rewards = r.Rewards,
        LostRuns = r.LostRuns,
        LostCargo = r.LostCargo,
        ManualEnds = r.ManualEnds,
        Rows = r.Rows,
        PickupClaims = r.PickupClaims,
        LockedTrips = r.LockedTrips,
        PastWorkers = r.PastWorkers,
        Carried = r.Carried,
        RunValues = r.RunValues,
        FailedValues = r.FailedValues,
        MissionIds = r.MissionIds,
        Party = r.Party,
        Stats = r.Stats,
        Received = r.Received,
        Members = r.Members,
    };

    /// <summary>The data of these rows (the lists in the order of their Position).</summary>
    public AppData ToAppData()
    {
        var s = Settings;
        var d = new AppData
        {
            PartyTrackingSince = s.PartyTrackingSince,
            PlanCreditSince = s.PlanCreditSince,
            GameLogPath = s.GameLogPath,
            DarkMode = s.DarkMode,
            Channel = s.Channel,
            WebView = s.WebView,
            WebViewPort = s.WebViewPort,
            WatchPaths = s.WatchPaths,
            DisabledWatchPaths = s.DisabledWatchPaths,
            Party = Members.Where(m => m.PartyPosition != null).OrderBy(m => m.PartyPosition).Select(m => m.Name).ToList(),
            InactiveParty = Members.Where(m => m.Inactive).Select(m => m.Name).ToList(),
        };
        foreach (var m in Members)
        {
            if (m.Ship != null) d.Ship[m.Name] = m.Ship;
            if (m.QuantumDrive != null) d.QuantumDrive[m.Name] = m.QuantumDrive;
            if (m.Color is int color) d.WorkerColors[m.Name] = color;
            if (m.ShipCapacity is int batches) d.ShipCapacity[m.Name] = batches;
            if (m.LocationId != null) d.StartPoints[m.Name] = new StartPoint { Id = m.LocationId, Since = m.LocationSince ?? default };
        }

        d.PartyChanges = PartyChanges.OrderBy(r => r.Position).Select(r => new PartyChange { Name = r.Name, TimeUtc = r.TimeUtc, Active = r.Active }).ToList();
        d.ManualSessions = ManualSessions.OrderBy(r => r.Position).Select(r => new ManualSession { StartUtc = r.StartUtc, StopUtc = r.StopUtc }).ToList();
        foreach (var r in Sessions.OrderBy(r => r.Position)) d.Sessions[r.Key] = SessionOf(r);
        d.Transfers = Transfers.OrderBy(r => r.Position).Select(r => new SavedTransfer { Time = r.Time, From = r.From, To = r.To, Amount = r.Amount, Payment = r.Payment }).ToList();
        d.Payouts = Payouts.OrderBy(r => r.Position).Select(r => new PayoutRecord
        {
            MissionId = r.MissionId, Time = r.Time, Contract = r.Contract, Payout = r.Payout, Members = r.Members, Each = r.Each,
        }).ToList();
        foreach (var r in MissionHolders)
        {
            if (!d.MissionHolders.TryGetValue(r.MissionId, out var holders)) d.MissionHolders[r.MissionId] = holders = new Dictionary<string, bool>();
            holders[r.Member] = r.Has;
        }
        foreach (var r in PayoutOverrides) d.PayoutOverrides[r.MissionId] = r.Payout;
        foreach (var r in DroppedMissions) d.DroppedMissions[r.MissionId] = r.Time;
        foreach (var r in TypedScu) d.TypedScu[r.Objective] = r.Scu;
        foreach (var r in PairAdjust) d.PairAdjust[r.Pair] = r.Amount;
        foreach (var r in LocationNames) d.LocationNames[r.LocationId] = r.Name;
        return d;
    }
}
