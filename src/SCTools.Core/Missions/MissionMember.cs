namespace SCTools.Missions;

/// <summary>
/// What a player's log said about another player of the same mission: "PlayerJoined" (he accepted the share) or "PlayerLeft" (he left it), with the other
/// player's id and when, by this player's clock. A player's own log never says how the mission ended for the others, only this.
/// </summary>
public sealed record MissionMember(string PlayerId, bool Left, DateTime Utc);
