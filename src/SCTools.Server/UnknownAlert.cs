namespace SCTools.Server;

/// <summary>A notice of an unknown place: its number, the text and what it is, who met it, whether it was one of the admin's accounts, when, the system.</summary>
public sealed record UnknownAlert(int Seq, string Text, UnknownKind Kind, string KindText, string Player, bool His, DateTime TimeUtc, string? System);
