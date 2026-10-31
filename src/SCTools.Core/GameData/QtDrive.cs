using System.Globalization;

namespace SCTools.GameData;

/// <summary>
/// A quantum drive, from the Star Citizen Wiki API (items, type QuantumDrive, "standard jump"). All speeds are in metres per second and
/// the accelerations in metres per second squared. Size 1 to 4, grade A to D. FuelRate: the game data's fuel_rate (0 when the data has none).
/// </summary>
public sealed record QtDrive(string Name, string Maker, int Size, string Grade, double Speed, double Accel1, double Accel2, double Engage, double FuelRate = 0)
{
    /// <summary>
    /// The fuel burn as the game shows it in the Vehicle Loadout Manager ("Quantum Fuel Burn: 141.68 per Km" for a TS-2): the game data's fuel_rate times a
    /// billion, or 0 when the data has none. The quantum route line's fuel estimate is this times the distance, times one factor for every ship (Frank's C2
    /// and Perseus, 31 Oct 2026).
    /// </summary>
    public double FuelPerKm => FuelRate * 1e9;

    public string Key => Name + " S" + Size;
    public string Label => Key;   // the name only (the speed in Mm/s used to be shown after it)

    /// <summary>
    /// The drive as crafted: its drive speed this many percent higher (or lower); the rest is the same (crafting changes only the drive speed: a TS-2 of
    /// quality 1000 goes 611,661,360 m/s instead of 509,717,800).
    /// </summary>
    public QtDrive Crafted(double percent) => percent == 0 ? this : this with { Speed = Speed * (1 + percent / 100) };

    /// <summary>
    /// Seconds for one jump of this many Gm (the time it shows for a distance in the star map, without the spool time).
    /// The drive speeds up with the stage 1 acceleration until it reaches the switch speed, then with the stage 2 acceleration up to the
    /// top speed, flies at the top speed and slows down the same way. A jump that is too short to reach the top speed
    /// speeds up and slows down halfway each. Checked against SPViewer with a TS-2 (S3, 12 jumps from 1 to 150 Gm, within 2 seconds),
    /// an XL-1 (S2) and a VK-00 (S1) (1, 15 and 100 Gm, within 1 second). The size 4 drives are only approximate (an Allegro is up to 10 seconds off).
    /// </summary>
    public double Seconds(double gm)
    {
        var dist = gm * 1e9;
        if (dist <= 0) return 0;
        var (rampTime, rampDist) = Ramp(Speed);
        if (2 * rampDist <= dist) return 2 * rampTime + (dist - 2 * rampDist) / Speed;
        // too short to reach the top speed: find the speed where the way up and the way down together use the whole distance (kept: the same drive and leg give
        // the same time, and the planner asks for them over and over)
        return ShortLegs.GetOrAdd((this, dist), k => k.Drive.ShortLeg(k.Dist));
    }

    static readonly System.Collections.Concurrent.ConcurrentDictionary<(QtDrive Drive, double Dist), double> ShortLegs = new();

    double ShortLeg(double dist)
    {
        double lo = 0, hi = Speed;
        for (var i = 0; i < 80; i++)
        {
            var mid = (lo + hi) / 2;
            if (2 * Ramp(mid).Dist > dist) hi = mid;
            else lo = mid;
        }
        return 2 * Ramp(lo).Time;
    }

    /// <summary>
    /// The speed where the drive goes from the stage 1 to the stage 2 acceleration. For size 2 and 3 it is the engage speed of the data.
    /// The data has 1500 m/s for size 1, but SPViewer and the other size 1 numbers fit about 48 Mm/s; size 4 fits about 90 Mm/s (roughly).
    /// </summary>
    public double SwitchSpeed => Size switch { 1 => 48e6, 4 => 90e6, _ => Engage };

    /// <summary>Time and distance of speeding up from standstill to a speed.</summary>
    (double Time, double Dist) Ramp(double to)
    {
        var s1 = Math.Min(to, SwitchSpeed);
        var t = s1 / Accel1;
        var d = s1 * s1 / (2 * Accel1);
        if (to > s1)
        {
            var t2 = (to - s1) / Accel2;
            t += t2;
            d += (s1 + to) / 2 * t2;
        }
        return (t, d);
    }
}
