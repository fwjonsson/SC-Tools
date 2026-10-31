namespace SCTools.Missions;

/// <summary>
/// What a screenshot of a contract in the game says, as the Mission Library's paste box read it (ContractCardReader): the contract's name, the Reward in aUEC,
/// "Contracted By" (the faction), the MG or Council scrip its description promises ("a bonus of 18 MG Scrip") and the reputation it gives (Xp: "Reputation
/// Awarded: 250", else the "[250 Rep]" of its name). Null: not read. Notes: what the admin should look at before saving (a reward not read for sure).
/// </summary>
public sealed record ContractCard(string? Title, long? Reward, long? Mg, long? Council, string? Contractor, IReadOnlyList<string> Notes, long? Xp = null);
