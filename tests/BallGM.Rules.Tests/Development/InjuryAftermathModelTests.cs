using BallGM.Domain.Players;
using BallGM.Domain.Randomness;
using BallGM.Rules.Development;

namespace BallGM.Rules.Tests.Development;

public sealed class InjuryAftermathModelTests
{
    private static readonly PlayerAttribute[] Athleticism = [PlayerAttribute.Speed, PlayerAttribute.FirstStep, PlayerAttribute.Vertical, PlayerAttribute.LateralQuickness, PlayerAttribute.Stamina];

    [Fact]
    public void AYoungPlayerBouncesBack_AVeteranLosesAStepForGood()
    {
        var sheet = new PlayerRating(80);

        var (young, youngNote) = InjuryAftermathModel.Apply(sheet, 22, 300, "Young Gun", "torn knee ligament", new SeededRandomSource(1));
        var (veteran, veteranNote) = InjuryAftermathModel.Apply(sheet, 33, 300, "Old Hand", "ruptured Achilles tendon", new SeededRandomSource(1));

        Assert.All(Athleticism, attribute => Assert.Equal(sheet[attribute], young[attribute]));
        Assert.True(young.Hidden(HiddenAttribute.InjuryProneness) > sheet.Hidden(HiddenAttribute.InjuryProneness));
        Assert.Contains("nothing lost", youngNote!.Explanation, StringComparison.Ordinal);

        Assert.All(Athleticism, attribute => Assert.True(veteran[attribute] < sheet[attribute] - 3, $"{attribute}: {veteran[attribute]}"));
        Assert.True(veteran.Overall < sheet.Overall);
        Assert.True(veteran.PotentialAbility <= veteran.Overall);
        Assert.Contains("lost a step", veteranNote!.Explanation, StringComparison.Ordinal);
    }

    [Fact]
    public void ALongerInjuryCostsMore_AndAShortOneLeavesNothing()
    {
        var sheet = new PlayerRating(80);
        int Lost(int days) => Athleticism.Sum(attribute => sheet[attribute] - InjuryAftermathModel.Apply(sheet, 32, days, "P", "fractured foot", new SeededRandomSource(4)).Rating[attribute]);

        Assert.True(Lost(320) > Lost(60));
        Assert.Equal(0, Lost(30));
        Assert.Null(InjuryAftermathModel.Apply(sheet, 32, 30, "P", "ankle sprain", new SeededRandomSource(4)).Finding);
    }
}
