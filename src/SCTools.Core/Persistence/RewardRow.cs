using System.Text.Json;

namespace SCTools.Persistence;

public sealed class RewardRow
{
    public decimal Reward { get; set; }
    public int Missions { get; set; }
}
