namespace SCTools.Locations;

/// <summary>
/// Where a mission's quantum beacon is, by the fuel estimate of the route to it (#403). The game writes the estimate with every route it works out
/// (QtEvent.FuelEstimate), and it is the drive's fuel burn (as the game shows it in Vehicles, "Quantum Fuel Burn: 130.61 per Km") times the distance in Gm
/// times EstimatePerKmGm: Frank's C2 (130.61 per km) gave 35,035 per Gm on four Pyro jumps, within 0.03 %, and his Perseus's 120.33 per km was predicted
/// from it to 0.03 % (31 Oct 2026). So the estimate over the ship's estimate per Gm is how far the beacon is from where the jump starts, and the hidden
/// spot of the game data at that distance (Atlas.Spots: the mission spots of Nyx's rings, the hidden stations) is where it is: the two bounties of that
/// night came out at the right spot, 1.2 and 0.4 Mm off, with the next spot over 370 Mm away.
/// </summary>
public static class BeaconRanging
{
    /// <summary>The route's fuel estimate for one Gm, over the drive's fuel burn per km.</summary>
    public const double EstimatePerKmGm = 268.2;

    /// <summary>A jump between two known places this long or longer tells a ship's estimate per Gm (a short one is too rough).</summary>
    public const double LearnGm = 1;

    /// <summary>How far off a spot can be: 0.3 % of the distance (the ship is somewhere in the station's area, not at its middle), at least 20 Mm.</summary>
    public static double Tolerance(double gm) => Math.Max(0.02, gm * 0.003);

    /// <summary>The hidden spot of the start's system this far (Gm) from the start, the one that fits best; null when none is within the tolerance.</summary>
    public static Place? Pick(Atlas atlas, Place from, double gm)
    {
        Place? best = null;
        var off = double.MaxValue;
        foreach (var s in atlas.Spots)
        {
            if (s.System != from.System) continue;
            var d = Math.Abs(StarMap.Distance(from, s) - gm);
            if (d < off) (best, off) = (s, d);
        }
        return off <= Tolerance(gm) ? best : null;
    }
}
