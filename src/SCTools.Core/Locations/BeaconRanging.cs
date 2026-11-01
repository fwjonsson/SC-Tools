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

    /// <summary>
    /// Spots that all fit a distance and lie this close together (Gm) are one answer: at the map's scale it makes no difference which one it is (the far side
    /// of the Keeger Belt, 95.9 Gm from location 27, had four within the tolerance, all within 2 Gm of each other).
    /// </summary>
    public const double SameArea = 5;

    /// <summary>
    /// The hidden spot of the start's system this far (Gm) from the start, the one that fits best; null when none is within the tolerance, or when spots far
    /// apart fit as well (a distance is a circle around the start: many asteroid bases have the same "Go to Asteroid Mining Base", and the more the players
    /// save, the more often two lie on it). Then the map does not guess (Frank, 1 Nov 2026): he is on his way to the mission, with no marker.
    /// </summary>
    public static Place? Pick(Atlas atlas, Place from, double gm)
    {
        var fits = atlas.Spots.Where(s => s.System == from.System)
            .Select(s => (Spot: s, Off: Math.Abs(StarMap.Distance(from, s) - gm)))
            .Where(x => x.Off <= Tolerance(gm)).OrderBy(x => x.Off).ToList();
        if (fits.Count == 0) return null;
        var best = fits[0].Spot;
        return fits.All(x => StarMap.Distance(x.Spot, best) <= SameArea) ? best : null;
    }
}
