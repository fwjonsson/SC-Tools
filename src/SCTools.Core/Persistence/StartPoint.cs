using System.Text.Json;

namespace SCTools.Persistence;

/// <summary>Where a worker is, when you set his Current location by hand on the Trip Planner. It ends when the logs show him picking up, delivering a load from Ruin or handing in something after that.</summary>
public sealed class StartPoint
{
    public string Id { get; set; } = "";
    public DateTime Since { get; set; }   // UTC
}
