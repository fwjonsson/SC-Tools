namespace SCTools.GameLog;

/// <summary>
/// The player of a log came into a ship's voice channel or out of it. Every player's ship has one, named by the ship and its owner, and the game puts
/// everybody who boards it in it: "You have joined channel 'RSI Perseus : zzoega'." in the log of the one who boarded (zzatvo's log, when he climbs into
/// zzoega's Perseus to be its gunner). It says "You have left the channel 'RSI Perseus : zzoega'." only now and then (746 of the 1,567 joins in Frank's
/// three accounts' logs of Aug-Oct 2026); a channel he came into and was not told he left stays his until he boards another ship (the Carrack's stayed
/// when they climbed out of the Pisces docked in it, Frank 31 Oct 2026). Ship is the name the game gives the ship ("Crusader C2 Hercules Starlifter"), not
/// its class; a ship of nobody's (an AI ship he steals) has no channel.
/// </summary>
public sealed record ShipChannel(DateTime Time, bool Joined, string Ship, string Owner);
