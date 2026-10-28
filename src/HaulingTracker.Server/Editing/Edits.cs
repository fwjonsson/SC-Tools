using System.Globalization;

namespace HaulingTracker.Server.Editing;

/// <summary>
/// The changes the pages make, the same as the program makes them (the Party table, Start / Stop Session, Next Stops, the Contracts table, the boards, the
/// Contracts and Debts tabs). Each one runs inside LiveTracker.Edit (under the lock, saved, worked out again) and says in a short sentence what it did.
/// </summary>
public static class Edits
{
    // ------------------------------------------------------------------ a member's own settings (My page, and the admin's Party page)

    /// <summary>
    /// His Current location (null = Automatic), colour (0xRRGGBB), ship and quantum drive: only what is different from now is changed. A location set by hand
    /// releases his locked next stop, so the plan is free to start with a stop near him (as in the program). A colour too close to another member's is not
    /// taken. What was changed, and what was refused, for the message.
    /// </summary>
    public static (List<string> Changed, string? Refused) ApplySettings(Tracker t, string name, string? location, int color, Ship? ship, QtDrive? drive)
    {
        var changed = new List<string>();
        string? refused = null;
        var (now, byHand) = t.Whereabouts.LocationOf(name);
        if ((location ?? "") != (byHand ? now : ""))
        {
            t.Whereabouts.SetLocation(name, location);
            t.Plan.ReleaseLock(name);
            changed.Add("location");
        }
        if (color != ColorOf(t, name))
        {
            if (TooCloseTo(t, name, color) is { } other)
            {
                refused = "The colour is not saved: it is too close to " + other + "'s. Pick one that stands out more.";
            }
            else
            {
                t.Crew.SetColor(name, unchecked((int)0xFF000000) | color);
                changed.Add("colour");
            }
        }
        if ((ship?.Name ?? "") != (t.Crew.ShipOf(name)?.Name ?? ""))
        {
            t.Crew.SetShip(name, ship?.Name);
            changed.Add("ship");
        }
        if ((drive?.Key ?? "") != (t.Crew.DriveOf(name)?.Key ?? ""))
        {
            t.Crew.SetDrive(name, drive?.Key);
            changed.Add("quantum drive");
        }
        return (changed, refused);
    }

    /// <summary>The other party member whose colour this one is too close to (see ViewPalette.Difference), or null.</summary>
    public static string? TooCloseTo(Tracker t, string name, int color) =>
        t.State.Data.Party.Where(n => !PlayerNames.SameName(n, name)).FirstOrDefault(n => ViewPalette.Difference(ColorOf(t, n), color) < ViewPalette.TooClose);

    /// <summary>A member's colour on the page and the map (0xRRGGBB): the one picked, or the built-in one.</summary>
    public static int ColorOf(Tracker t, string name) =>
        ViewPalette.WorkerColor(name, t.Crew.PickedColor(name), t.State.Data.Party.FindIndex(n => PlayerNames.SameName(n, name)));

    /// <summary>"Saved: colour, ship." or "Nothing changed.", and what was refused.</summary>
    public static string Saved(string who, (List<string> Changed, string? Refused) result)
    {
        var said = result.Changed.Count == 0 ? (result.Refused == null ? "Nothing changed." : "") :
            "Saved" + (who.Length > 0 ? " for " + who : "") + ": " + string.Join(", ", result.Changed) + ".";
        return result.Refused == null ? said : (said + " " + result.Refused).Trim();
    }

    // ------------------------------------------------------------------ the party

    public static string AddMember(Tracker t, string name, DateTime nowUtc)
    {
        name = name.Trim();
        if (name.Length == 0) return "Type a name.";
        if (!t.Crew.Join(name)) return t.State.Data.Party.First(x => PlayerNames.SameName(x, name)) + " is already in the party.";
        var member = t.State.Data.Party.First(x => PlayerNames.SameName(x, name));
        if (t.State.Data.AutoPartySince == null) return member + " joined the party.";
        t.Crew.SetMemberActive(member, true, nowUtc, PartyChange.BackToLogs);   // the logs say whether he is playing
        return member + " is in the party. He is active while the logs show him playing.";
    }

    public static string RemoveMember(Tracker t, string name)
    {
        var member = t.State.Data.Party.FirstOrDefault(x => PlayerNames.SameName(x, name));
        if (member == null) return name + " is not in the party.";
        t.Crew.Remove(member);
        return member + " was removed from the party. His earlier work and debts stay; he just stops getting a share of new payouts.";
    }

    /// <summary>A member's own settings as they are now (Location null = Automatic), to change one of them on the Party page.</summary>
    public sealed record OwnSettings(string? Location, int Color, Ship? Ship, QtDrive? Drive);

    static string? Member(Tracker t, string name) => t.State.Data.Party.FirstOrDefault(x => PlayerNames.SameName(x, name));

    static OwnSettings OwnOf(Tracker t, string member)
    {
        var (location, byHand) = t.Whereabouts.LocationOf(member);
        return new OwnSettings(byHand ? location : null, ColorOf(t, member), t.Crew.ShipOf(member), t.Crew.DriveOf(member));
    }

    /// <summary>
    /// Active on the Party page, saved with its time (so the payouts know who was there). Where the logs decide (the server): Auto (null) goes back to what the
    /// logs say, In (true) or Out (false) holds until the logs change their mind about him. Elsewhere it is the tick as in the program.
    /// </summary>
    public static string SetActive(Tracker t, string name, bool? active, DateTime nowUtc)
    {
        var member = Member(t, name);
        if (member == null) return name + " is not in the party.";
        if (t.State.Data.AutoPartySince == null)
        {
            if (active == null || active == t.Crew.IsActiveAt(member, nowUtc)) return "Nothing changed.";
            t.Crew.SetMemberActive(member, active.Value, nowUtc);
            return member + (active.Value ? " joined: mission payouts from now on are split with him." : " left: no share of payouts from now on.");
        }
        if (active == t.Crew.OverrideOf(member)) return "Nothing changed.";
        if (active == null)
        {
            t.Crew.SetMemberActive(member, t.Crew.IsActiveAt(member, nowUtc), nowUtc, PartyChange.BackToLogs);   // what the logs say then is filled in at once
            return member + " follows the logs again.";
        }
        t.Crew.SetMemberActive(member, active.Value, nowUtc, PartyChange.ByAdmin);
        return member + (active.Value ? " is In until the logs show him leaving." : " is Out until the logs show him playing.");
    }

    /// <summary>One of a member's own settings on the Party page (the others stay as they are): his location, colour, ship or quantum drive.</summary>
    public static string SetOwn(Tracker t, string name, Func<OwnSettings, OwnSettings> change)
    {
        var member = Member(t, name);
        if (member == null) return name + " is not in the party.";
        var s = change(OwnOf(t, member));
        return Saved(member, ApplySettings(t, member, s.Location, s.Color, s.Ship, s.Drive));
    }

    // ------------------------------------------------------------------ the session

    /// <summary>The session started with Start Session that is still running, or null.</summary>
    public static ManualSession? OpenSession(Tracker t) => t.State.Data.ManualSessions.LastOrDefault(s => !s.StopUtc.HasValue);

    /// <summary>Start Session / Stop Session: one button, as in the program.</summary>
    public static string ToggleSession(Tracker t, DateTime nowUtc)
    {
        var open = OpenSession(t);
        if (open == null)
        {
            t.State.Data.ManualSessions.Add(new ManualSession { StartUtc = nowUtc });
            return "Session started. Only what happens from now on is read, until Stop Session.";
        }
        open.StopUtc = nowUtc;
        return "Session stopped.";
    }

    /// <summary>Next Stops: Locked / Unlocked, as the button of the Trip Planner.</summary>
    public static string ToggleLocks(Tracker t)
    {
        if (t.State.Inputs == null) return "There is no session to plan.";
        if (t.State.Inputs.NextStopsLocked)
        {
            t.Plan.Unlock();
            return "The next stops are unlocked: the plan is free.";
        }
        var locked = t.Plan.LockNextStops();
        return "The next stops are locked" + (locked > 0 ? " (" + locked + (locked == 1 ? " worker)." : " workers).") : ".");
    }

    /// <summary>
    /// The Mission Reward of every contract line (in the order of Earnings.RewardLines); null or 0 = the built-in reward. The session shown gets them (when
    /// there is one), and every new session starts with them.
    /// </summary>
    public static string SetRewards(Tracker t, IReadOnlyList<decimal?> rewards)
    {
        var inp = t.State.Inputs;
        for (var i = 0; i < Earnings.RewardLines.Count && i < rewards.Count; i++) Earnings.SetReward(t.State.Data, inp, Earnings.RewardLines[i], rewards[i] ?? 0);
        return inp == null ? "The mission rewards are saved: the next session starts with them." : "The mission rewards are saved, also for the next sessions.";
    }

    /// <summary>The start time of the session, typed as HH:mm (the session time runs from it); empty = from the logs.</summary>
    public static string SetStartTime(Tracker t, string? typed)
    {
        var inp = t.State.Inputs;
        if (inp == null) return "There is no session.";
        typed = typed?.Trim() ?? "";
        if (typed.Length == 0)
        {
            inp.Start = null;
            inp.End = null;
            return "The session time comes from the logs again.";
        }
        if (!TimeSpan.TryParseExact(typed, new[] { "h\\:mm", "hh\\:mm" }, CultureInfo.InvariantCulture, out var time) || time >= TimeSpan.FromDays(1))
            return "Type the start time as 19:30.";
        inp.Start = time.ToString("hh\\:mm", CultureInfo.InvariantCulture);
        return "The session time runs from " + inp.Start + ".";
    }

    // ------------------------------------------------------------------ the stops and the runs

    /// <summary>Your own name for a pickup location; nothing (or the name the program knows) gives it back the program's name.</summary>
    public static string RenameStop(Tracker t, string zone, string name)
    {
        name = name.Trim();
        var reverted = t.Places.Rename(zone, name);
        return reverted ? "The location has the name the program knows: " + t.Places.LocationName(zone) + "." : "The location is called " + name + " now.";
    }

    public static string SetNotes(Tracker t, string zone, string notes)
    {
        if (t.State.Inputs == null) return "There is no session.";
        t.State.RowOf(zone).Notes = notes.Trim();
        return "The notes are saved.";
    }

    /// <summary>Lost Cargo of a run: None, Little or All (All = the run failed: one still on the way ends, and picking something else brings it back).</summary>
    public static string SetLost(Tracker t, string runKey, string level)
    {
        if (level is not ("None" or "Little" or "All")) return "Pick None, Little or All.";
        var run = t.State.Trips.FirstOrDefault(r => r.Key == runKey);
        if (run == null) return "That run is not in the session any more.";
        t.Runs.SetLostLevel(run, level);
        return "Run " + run.Number + " of " + run.Worker + ": lost cargo " + level + ".";
    }

    /// <summary>The SCU of a drop-off the game did not tell the size of ("mission id|objective id"); 0 forgets it. The game's own number wins once it writes it.</summary>
    public static string SetScu(Tracker t, string key, int scu)
    {
        if (scu < 0 || scu > 10000) return "Type the SCU from the contract (0 to forget it).";
        var label = t.DropOffs.DropLabel(key);
        t.DropOffs.SetTypedScu(key, scu);
        return scu > 0 ? label + ": " + scu + " SCU." : label + ": the SCU is forgotten.";
    }

    // ------------------------------------------------------------------ the contracts

    static Mission? MissionOf(Tracker t, string id) => t.Contracts.ContractRows().FirstOrDefault(m => m.Id == id);

    /// <summary>Who has a mission (a box of the Contracts table): it wins over the logs until it is the same as what they say.</summary>
    public static string SetHolder(Tracker t, string missionId, string member, bool has)
    {
        var m = MissionOf(t, missionId);
        if (m == null) return "That mission is not going any more.";
        if (m.Dropped) return "That mission is marked as gone: bring it back first.";
        var name = t.State.Data.Party.FirstOrDefault(x => PlayerNames.SameName(x, member));
        if (name == null) return member + " is not in the party.";
        t.Contracts.SetHolder(m, name, has);
        return name + (has ? " has " : " does not have ") + Contracts.ContractName(m) + (string.IsNullOrEmpty(m.Cargo) ? "" : " (" + m.Cargo + ")") + ".";
    }

    /// <summary>Gone / Bring back: a mission that is not in the game any more counts as abandoned everywhere (planner, session, payouts).</summary>
    public static string ToggleGone(Tracker t, string missionId)
    {
        var m = MissionOf(t, missionId);
        if (m == null) return "That mission is not going any more.";
        var wasDropped = m.Dropped;
        t.Contracts.ToggleGone(m);
        return Contracts.ContractName(m) + (wasDropped ? " counts again." : " is marked as gone: it is not planned or counted.");
    }

    // ------------------------------------------------------------------ the debts

    /// <summary>The key of a money transfer on the page (it has no id of its own): its time, who to whom, and the amount.</summary>
    public static string TransferKey(SavedTransfer x) =>
        x.Time.Ticks.ToString(CultureInfo.InvariantCulture) + "|" + x.From + "|" + x.To + "|" + x.Amount.ToString(CultureInfo.InvariantCulture);

    /// <summary>Payment: the transfer pays off a debt (it counts against the balances).</summary>
    public static string SetPayment(Tracker t, string key, bool payment)
    {
        var x = t.State.Data.Transfers.FirstOrDefault(tr => TransferKey(tr) == key);
        if (x == null) return "That transfer is not there any more.";
        x.Payment = payment;
        return x.From + " to " + x.To + " " + x.Amount.ToString("N0", CultureInfo.CurrentCulture) + (payment ? " pays off a debt." : " is not a payment.");
    }

    /// <summary>What a mission really paid (a short delivery pays less); 0 or nothing = the reward of its contract again.</summary>
    public static string SetPayout(Tracker t, string missionId, decimal? payout)
    {
        var r = t.State.Data.Payouts.FirstOrDefault(p => p.MissionId == missionId);
        if (r == null) return "That payout is not there any more.";
        t.Payouts.SetPayoutOverride(missionId, payout ?? 0);
        return (payout ?? 0) > 0 ? r.Contract + " paid " + payout!.Value.ToString("N0", CultureInfo.CurrentCulture) + "." : r.Contract + " paid the reward of its contract.";
    }

    /// <summary>The balances of a member with every other one: worked out, plus what was typed over them.</summary>
    public static List<(string Other, decimal Calculated, decimal Shown)> Balances(Tracker t, string me)
    {
        var data = t.State.Data;
        var pairs = DebtCalc.Pairs(data.Sessions.Values, data.Transfers);
        return t.Payouts.DebtPartyNames().Where(o => !PlayerNames.SameName(o, me))
            .Select(o =>
            {
                var calc = DebtCalc.Between(pairs, me, o);
                return (o, calc, calc + t.Payouts.GetAdj(me, o));
            })
            .ToList();
    }

    /// <summary>A balance typed over (+ the other owes me, − I owe him): the difference to the worked-out one is kept as a correction.</summary>
    public static string SetBalance(Tracker t, string me, string other, decimal balance)
    {
        var line = Balances(t, me).FirstOrDefault(b => PlayerNames.SameName(b.Other, other));
        if (line.Other == null) return other + " is not in the debts.";
        t.Payouts.SetAdj(me, line.Other, balance - line.Calculated);
        return Payouts.Meaning(me, line.Other, balance) + ".";
    }

    /// <summary>Session Party: who was in the party this session (the payouts of the session are split between them).</summary>
    public static string SetSessionParty(Tracker t, IEnumerable<string> names)
    {
        var inp = t.State.Inputs;
        if (inp == null) return "There is no session.";
        var all = t.Payouts.DebtPartyNames();
        inp.Party = all.Where(n => names.Any(x => PlayerNames.SameName(x, n))).ToList();
        return inp.Party.Count == 0 ? "Nobody is in the party of this session." : "The party of this session: " + string.Join(", ", inp.Party) + ".";
    }
}
