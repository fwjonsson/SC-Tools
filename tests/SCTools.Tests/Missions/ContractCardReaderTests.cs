using SCTools.Missions;

namespace SCTools.Tests.Missions;

/// <summary>
/// What the text recognition read from a contract screenshot made into values (#437). The texts are what Tesseract read from Frank's screenshots of 31 Oct
/// 2026 (3840x1600): a hauling contract, a patrol with MG scrip, a Vanduul kill, a bounty, one with a lamp shining on the reward, an offer with Council scrip.
/// </summary>
public class ContractCardReaderTests
{
    [Fact]
    public void The_reward_the_scrip_and_the_faction_of_a_card()
    {
        var patrol = ContractCardReader.Read("Patrol to Prevent Possible Attack [ 250 Rep]\n[BPT*\n", "235,000\n", "Intersec Defense Solutions\n",
            "Currently, we need someone who can help patrol a highly\ndangerous sector. Complete the patrol and we'll send the main contractor a bonus of\n18 MG Scrip.\n");
        Assert.Equal(("Patrol to Prevent Possible Attack [ 250 Rep] [BPT*", 235_000L, 18L, (long?)null, "Intersec Defense Solutions"),
            (patrol.Title, patrol.Reward!.Value, patrol.Mg!.Value, patrol.Council, patrol.Contractor));
        Assert.Empty(patrol.Notes);

        // the number of scrip on one line, "Council Scrip" on the next
        var offer = ContractCardReader.Read("Looking For Trouble [200 Rep]", "158,750", "a Headtiunters", "You make it to the end and I'll give the main contractor a bonus 14\nCouncil Scrip as thanks.");
        Assert.Equal((158_750L, (long?)null, 14L, "Headtiunters"), (offer.Reward!.Value, offer.Mg, offer.Council!.Value, offer.Contractor));   // the reflection's "a" is gone

        // Red Lvl. Contract: Defend Fuel Tanks (Foxwell Enforcement, 31 Oct 2026): the scrip in the middle of a sentence, the reputation at the end
        var fuel = ContractCardReader.Read("Red Lvl. Contract: Defend Fuel Tanks [250\nRep] [BP]*", "387,000", "Foxwell Enforcement",
            "If you and the\nassets make it out intact, we're offering a well-earned 7 MG Scrip\nto the main contractor.\n\nReputation Awarded: 250");
        Assert.Equal((387_000L, 7L, 250L), (fuel.Reward!.Value, fuel.Mg!.Value, fuel.Xp!.Value));
    }

    [Fact]
    public void A_reward_not_read_for_sure_is_not_a_number()
    {
        Assert.Equal(35_000, ContractCardReader.Reward("35,000"));
        Assert.Equal(252_250, ContractCardReader.Reward("252.250"));   // a comma read as a dot
        Assert.Equal(1_370_500, ContractCardReader.Reward("1,370,500"));   // Retrieve Additional Smuggler Intel: a million and more
        Assert.Equal("Intersec Defense Solutions", ContractCardReader.Contractor("i Intersec Defense Solutions"));   // a reflection's "i"
        Assert.Null(ContractCardReader.Reward("1235,000"));   // the currency sign read as a 1 (the commas are not every three digits)
        Assert.Null(ContractCardReader.Reward("2"));   // the lamp: a stray mark
        Assert.Null(ContractCardReader.Reward(""));
        var lamp = ContractCardReader.Read("Reclusive Bounty: Lamond Carrigan | Very-High\nRisk [250 Rep]", "2", "Se", "");
        Assert.Null(lamp.Reward);
        Assert.Contains(lamp.Notes, n => n.Contains("type it in", StringComparison.Ordinal));
    }

    [Fact]
    public void A_bounty_s_lone_I_is_its_bar()
    {
        Assert.Equal("Reclusive Bounty: Lamond Carrigan | Very-High Risk [250 Rep]", ContractCardReader.Title("Reclusive Bounty: Lamond Carrigan I Very-High\nRisk [250 Rep]"));
        Assert.Equal("reclusive bounty | very-high risk", ContractCardReader.Plain("Reclusive Bounty: Lamond Carrigan | Very-High Risk [250 Rep]"));   // its kind, no marks
    }

    [Fact]
    public void The_faction_is_the_nearest_known_one()
    {
        var factions = new[] { "Headhunters", "Bounty Hunters Guild", "Intersec Defense Solutions", "Red Wind Linehaul" };
        Assert.Equal("Headhunters", ContractCardReader.Faction("Headtiunters", factions));
        Assert.Equal("Red Wind Linehaul", ContractCardReader.Faction("Red Wind Linehaul", factions));
        Assert.Null(ContractCardReader.Faction("Se", factions));
    }
}
