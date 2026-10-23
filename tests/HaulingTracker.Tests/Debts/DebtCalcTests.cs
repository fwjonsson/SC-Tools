namespace HaulingTracker.Tests.Debts;

/// <summary>Who owes whom: everybody is owed the value he carried and owes the share of the payouts he received.</summary>
public class DebtCalcTests
{
    static SessionInputs Session(Dictionary<string, decimal> carried, Dictionary<string, decimal> received) =>
        new() { Carried = carried, Received = received };

    [Fact]
    public void Who_carried_more_than_he_received_is_owed_the_difference()
    {
        var s = Session(
            new() { ["Pilot_A"] = 3_000_000m, ["Pilot_B"] = 1_000_000m },
            new() { ["Pilot_A"] = 2_000_000m, ["Pilot_B"] = 2_000_000m });
        var nets = DebtCalc.Nets(new[] { s }, Array.Empty<SavedTransfer>());
        Assert.Equal(1_000_000m, nets["Pilot_A"]);
        Assert.Equal(-1_000_000m, nets["Pilot_B"]);

        var settle = DebtCalc.Settle(nets);
        var (debtor, creditor, amount) = Assert.Single(settle);
        Assert.Equal(("Pilot_B", "Pilot_A", 1_000_000m), (debtor, creditor, amount));
        Assert.Equal(1_000_000m, DebtCalc.Between(settle, "Pilot_A", "Pilot_B"));
        Assert.Equal(-1_000_000m, DebtCalc.Between(settle, "Pilot_B", "Pilot_A"));
    }

    [Fact]
    public void A_payment_settles_the_debt()
    {
        var s = Session(
            new() { ["Pilot_A"] = 3_000_000m, ["Pilot_B"] = 1_000_000m },
            new() { ["Pilot_A"] = 2_000_000m, ["Pilot_B"] = 2_000_000m });
        var paid = new SavedTransfer { From = "Pilot_B", To = "Pilot_A", Amount = 1_000_000m, Payment = true };
        var nets = DebtCalc.Nets(new[] { s }, new[] { paid });
        Assert.Equal(0m, nets["Pilot_A"]);
        Assert.Equal(0m, nets["Pilot_B"]);
        Assert.Empty(DebtCalc.Settle(nets));
    }

    [Fact]
    public void A_transfer_that_is_not_a_payment_changes_nothing()
    {
        var gift = new SavedTransfer { From = "Pilot_B", To = "Pilot_A", Amount = 50_000m, Payment = false };
        Assert.Empty(DebtCalc.Nets(Array.Empty<SessionInputs>(), new[] { gift }));
    }

    [Fact]
    public void Settling_three_people_takes_the_fewest_payments()
    {
        var nets = new Dictionary<string, decimal> { ["A"] = 900m, ["B"] = -600m, ["C"] = -300m };
        var settle = DebtCalc.Settle(nets);
        Assert.Equal(2, settle.Count);
        Assert.Equal(900m, settle.Where(s => s.Creditor == "A").Sum(s => s.Amount));
    }
}
