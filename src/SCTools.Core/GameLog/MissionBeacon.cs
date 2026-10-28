namespace SCTools.GameLog;

/// <summary>
/// A mission objective's quantum beacon, as the game wrote it when it paired the objective's marker with it: "&lt;CObjectiveMarkerComponent::PairWithMissionNavpoint&gt;
/// MissionObjectiveMarker_21517[21517] - Paired with NavPoint: MISSION_QT_Quantum_Beacon_LongRange_865476577647[865476577647]". The marker (its entity id) is
/// the one of "Creating objective marker: missionId [...], ..., objectiveId [...], markerEntityId [21517]". A player jumps to the objective by its beacon.
/// </summary>
public sealed record MissionBeacon(DateTime Time, string NavPoint, string Marker, string MissionId, string Objective);
