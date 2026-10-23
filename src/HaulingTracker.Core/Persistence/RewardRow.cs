using System.Text.Json;

namespace HaulingTracker.Persistence;

public sealed class RewardRow
{
    public decimal Reward { get; set; }
    public int Missions { get; set; }
}
