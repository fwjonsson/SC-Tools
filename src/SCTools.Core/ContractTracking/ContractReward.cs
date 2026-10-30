namespace SCTools.ContractTracking;

/// <summary>What a contract pays, or one player's part of it: aUEC, MG scrip and Council scrip (null: not known).</summary>
public sealed record ContractReward(long? Auec, long? Mg, long? Council)
{
    /// <summary>Nothing is known about what it pays.</summary>
    public static readonly ContractReward Unknown = new(null, null, null);
}
