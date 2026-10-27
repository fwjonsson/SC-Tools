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

    [Fact]
    public void Pair_by_pair_who_carried_more_is_owed_by_those_who_carried_less()
    {
        var s = Session(
            new() { ["A"] = 1_900m, ["B"] = 400m, ["C"] = 700m },
            new() { ["A"] = 1_000m, ["B"] = 1_000m, ["C"] = 1_000m });   // A +900, B -600, C -300
        var pairs = DebtCalc.Pairs(new[] { s }, Array.Empty<SavedTransfer>());
        Assert.Equal(600m, DebtCalc.Between(pairs, "A", "B"));
        Assert.Equal(300m, DebtCalc.Between(pairs, "A", "C"));
        Assert.Equal(-600m, DebtCalc.Between(pairs, "B", "A"));
        Assert.Equal(0m, DebtCalc.Between(pairs, "B", "C"));
    }

    [Fact]
    public void A_payment_counts_between_the_two_who_made_it_and_moves_no_debt_to_anybody_else()
    {
        // two accounts of one player and a friend: zzatvo carried more than his share (zzoega owes him), Bileh carried a little; zzoega paid Bileh a lot
        var first = Session(new() { ["zzatvo"] = 3_000m, ["zzoega"] = 1_000m }, new() { ["zzatvo"] = 2_000m, ["zzoega"] = 2_000m });
        var second = Session(new() { ["bileh123"] = 500m, ["zzoega"] = 1_500m }, new() { ["bileh123"] = 0m, ["zzoega"] = 2_000m });
        var paid = new SavedTransfer { From = "zzoega", To = "Bileh123", Amount = 2_000m, Payment = true };
        var pairs = DebtCalc.Pairs(new[] { first, second }, new[] { paid });

        Assert.Equal(1_500m, DebtCalc.Between(pairs, "zzoega", "bileh123"));   // Bileh was owed 500 and got 2 000: he owes zzoega 1 500
        Assert.Equal(-1_000m, DebtCalc.Between(pairs, "zzoega", "zzatvo"));    // the payment to Bileh does not change what zzoega owes zzatvo
        Assert.Equal(0m, DebtCalc.Between(pairs, "zzatvo", "bileh123"));

        // the balances of everybody add up to his net
        var nets = DebtCalc.Nets(new[] { first, second }, new[] { paid });
        foreach (var me in new[] { "zzoega", "zzatvo", "bileh123" })
        {
            var sum = new[] { "zzoega", "zzatvo", "bileh123" }.Where(o => o != me).Sum(o => DebtCalc.Between(pairs, me, o));
            Assert.Equal(nets[me], sum);
        }
    }

    [Fact]
    public void A_session_without_saved_values_owes_nothing()
    {
        var empty = Session(new(), new() { ["A"] = 1_000m });
        Assert.Empty(DebtCalc.Pairs(new[] { empty }, Array.Empty<SavedTransfer>()));
    }
}
