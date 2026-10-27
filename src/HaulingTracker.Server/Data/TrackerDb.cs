using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace HaulingTracker.Server.Data;

/// <summary>
/// The database of the web app (SQLite, one file): the party and its members, the sessions, the transfers and payouts, what was typed on the Contracts and
/// Debts tabs, and the keys of the trackers. A change to these tables needs a migration (see Data/Migrations and README).
/// </summary>
public sealed class TrackerDb : DbContext
{
    public TrackerDb(DbContextOptions<TrackerDb> options) : base(options)
    {
    }

    public DbSet<SettingsRow> Settings => Set<SettingsRow>();
    public DbSet<MemberRow> Members => Set<MemberRow>();
    public DbSet<PartyChangeRow> PartyChanges => Set<PartyChangeRow>();
    public DbSet<ManualSessionRow> ManualSessions => Set<ManualSessionRow>();
    public DbSet<SessionRow> Sessions => Set<SessionRow>();
    public DbSet<TransferRow> Transfers => Set<TransferRow>();
    public DbSet<PayoutRow> Payouts => Set<PayoutRow>();
    public DbSet<MissionHolderRow> MissionHolders => Set<MissionHolderRow>();
    public DbSet<PayoutOverrideRow> PayoutOverrides => Set<PayoutOverrideRow>();
    public DbSet<DroppedMissionRow> DroppedMissions => Set<DroppedMissionRow>();
    public DbSet<TypedScuRow> TypedScu => Set<TypedScuRow>();
    public DbSet<PairAdjustRow> PairAdjust => Set<PairAdjustRow>();
    public DbSet<LocationNameRow> LocationNames => Set<LocationNameRow>();
    public DbSet<TrackerKeyRow> TrackerKeys => Set<TrackerKeyRow>();

    /// <summary>
    /// A time is kept as text with its kind (2026-10-27T18:49:00.0000000Z for UTC), so it comes back the same: SQLite has no time type, and a UTC time read
    /// back as "unspecified" would be shown an hour off.
    /// </summary>
    static readonly ValueConverter<DateTime, string> Time = new(
        v => v.ToString("O", CultureInfo.InvariantCulture),
        s => DateTime.Parse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind));

    /// <summary>An amount is kept as text exactly as it is written (1056875 stays 1056875, not 1056875.0).</summary>
    static readonly ValueConverter<decimal, string> Amount = new(
        v => v.ToString(CultureInfo.InvariantCulture),
        s => decimal.Parse(s, NumberStyles.Number, CultureInfo.InvariantCulture));

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<SettingsRow>(e =>
        {
            e.ToTable("Settings");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.WatchPaths).AsJson();
            e.Property(x => x.DisabledWatchPaths).AsJson();
        });

        b.Entity<MemberRow>(e =>
        {
            e.ToTable("Members");
            e.HasKey(x => x.Name);
        });

        b.Entity<PartyChangeRow>(e =>
        {
            e.ToTable("PartyChanges");
            e.HasKey(x => x.Position);
            e.Property(x => x.Position).ValueGeneratedNever();
        });

        b.Entity<ManualSessionRow>(e =>
        {
            e.ToTable("ManualSessions");
            e.HasKey(x => x.Position);
            e.Property(x => x.Position).ValueGeneratedNever();
        });

        b.Entity<SessionRow>(e =>
        {
            e.ToTable("Sessions");
            e.HasKey(x => x.Key);
            e.Property(x => x.Rewards).AsJson();
            e.Property(x => x.LostRuns).AsJson();
            e.Property(x => x.LostCargo).AsJson();
            e.Property(x => x.ManualEnds).AsJson();
            e.Property(x => x.Rows).AsJson();
            e.Property(x => x.PickupClaims).AsJson();
            e.Property(x => x.LockedTrips).AsJson();
            e.Property(x => x.PastWorkers).AsJson();
            e.Property(x => x.Carried).AsJson();
            e.Property(x => x.RunValues).AsJson();
            e.Property(x => x.FailedValues).AsJson();
            e.Property(x => x.MissionIds).AsJson();
            e.Property(x => x.Party).AsJson();
            e.Property(x => x.Stats).AsJson();
            e.Property(x => x.Received).AsJson();
            e.Property(x => x.Members).AsJson();
        });

        b.Entity<TransferRow>(e =>
        {
            e.ToTable("Transfers");
            e.HasKey(x => x.Position);
            e.Property(x => x.Position).ValueGeneratedNever();
        });

        b.Entity<PayoutRow>(e =>
        {
            e.ToTable("Payouts");
            e.HasKey(x => x.Position);
            e.Property(x => x.Position).ValueGeneratedNever();
            e.Property(x => x.Members).AsJson();
        });

        b.Entity<MissionHolderRow>(e =>
        {
            e.ToTable("MissionHolders");
            e.HasKey(x => new { x.MissionId, x.Member });
        });

        b.Entity<PayoutOverrideRow>(e =>
        {
            e.ToTable("PayoutOverrides");
            e.HasKey(x => x.MissionId);
        });

        b.Entity<DroppedMissionRow>(e =>
        {
            e.ToTable("DroppedMissions");
            e.HasKey(x => x.MissionId);
        });

        b.Entity<TypedScuRow>(e =>
        {
            e.ToTable("TypedScu");
            e.HasKey(x => x.Objective);
        });

        b.Entity<PairAdjustRow>(e =>
        {
            e.ToTable("PairAdjust");
            e.HasKey(x => x.Pair);
        });

        b.Entity<LocationNameRow>(e =>
        {
            e.ToTable("LocationNames");
            e.HasKey(x => x.LocationId);
        });

        b.Entity<TrackerKeyRow>(e =>
        {
            e.ToTable("TrackerKeys");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.KeyHash).IsUnique();
        });

        // every time with its kind, every amount as it is written (see Time and Amount)
        foreach (var entity in b.Model.GetEntityTypes())
        {
            foreach (var p in entity.GetProperties())
            {
                if (p.ClrType == typeof(DateTime) || p.ClrType == typeof(DateTime?)) p.SetValueConverter(Time);
                if (p.ClrType == typeof(decimal) || p.ClrType == typeof(decimal?)) p.SetValueConverter(Amount);
            }
        }
    }
}
