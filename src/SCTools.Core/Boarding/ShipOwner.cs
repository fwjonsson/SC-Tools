namespace SCTools.Boarding;

/// <summary>A ship of the game by its id (ShipBook.Ships): its name and owner, as the voice channel of the players aboard said them, and its class ("RSI_Perseus").</summary>
public sealed record ShipOwner(string Ship, string Owner, string Vehicle);
