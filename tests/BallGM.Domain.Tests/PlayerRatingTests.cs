using BallGM.Domain.Players;

namespace BallGM.Domain.Tests;

public sealed class PlayerRatingTests
{
    [Fact]
    public void AdjustAppliesADeltaWithinRange()
    {
        var rating = new PlayerRating(overall: 50).Adjust(5);

        Assert.Equal(55, rating.Overall);
    }

    [Fact]
    public void AdjustClampsAtTheTopOfTheScale()
    {
        var rating = new PlayerRating(overall: 98).Adjust(10);

        Assert.Equal(PlayerRating.MaximumOverall, rating.Overall);
    }

    [Fact]
    public void AdjustClampsAtTheBottomOfTheScale()
    {
        var rating = new PlayerRating(overall: 2).Adjust(-10);

        Assert.Equal(PlayerRating.MinimumOverall, rating.Overall);
    }

    [Fact]
    public void TheFlatConstructorSetsEveryVisibleAttributeToTheValueAndHiddenOnesToTheMiddle()
    {
        var rating = new PlayerRating(72);

        Assert.All(PlayerAttributes.All, attribute => Assert.Equal(72, rating[attribute]));
        Assert.All(PlayerAttributes.AllHidden, attribute => Assert.Equal(PlayerRating.NeutralHidden, rating.Hidden(attribute)));
        Assert.Equal(72, rating.Overall);
        Assert.Equal(72, rating.PotentialAbility);
        Assert.Null(rating.Body);
        Assert.Empty(rating.Traits);
    }

    [Fact]
    public void CurrentAbilityWeightsTheAttributes_AndTheWeightsTotalAHundred()
    {
        Assert.Equal(100, PlayerAttributes.All.Sum(PlayerAttributes.AbilityWeight));

        // Three-point shooting counts five times as much as dunking.
        var shooter = new PlayerRating(60).With(PlayerAttribute.ThreePoint, 80);
        var dunker = new PlayerRating(60).With(PlayerAttribute.Dunking, 80);

        Assert.Equal(61, shooter.Overall);
        Assert.Equal(60, dunker.Overall);
    }

    [Fact]
    public void EveryAttributeBelongsToItsGroup_InSheetOrder()
    {
        var groups = PlayerAttributes.All.Select(PlayerAttributes.GroupOf).ToList();

        Assert.Equal(groups.OrderBy(group => group), groups);
        Assert.Equal(Enum.GetValues<AttributeGroup>(), groups.Distinct());
        Assert.Equal(AttributeGroup.Mental, PlayerAttributes.GroupOf(PlayerAttribute.BasketballIq));
        Assert.Equal(AttributeGroup.Rebounding, PlayerAttributes.GroupOf(PlayerAttribute.BoxingOut));
    }

    [Fact]
    public void ASheetOutOfScaleOrShortOfAttributesIsRefused()
    {
        var full = Enumerable.Repeat(50, PlayerAttributes.Count).ToArray();
        var hidden = Enumerable.Repeat(50, PlayerAttributes.HiddenCount).ToArray();

        Assert.Throws<ArgumentException>(() => new PlayerRating(full[1..], hidden, 50));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PlayerRating(full.Select((value, index) => index == 3 ? 101 : value).ToArray(), hidden, 50));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PlayerRating(full, hidden, -1));
    }

    [Fact]
    public void AdjustMovesEveryVisibleAttributeButNotTheHiddenLayerOrTheBody()
    {
        var body = new PlayerBody(201, 208, 104);
        var rating = new PlayerRating(60).With(HiddenAttribute.Professionalism, 90).WithBody(body).With(PlayerAttribute.Passing, 99);
        var adjusted = rating.Adjust(5);

        Assert.Equal(100, adjusted[PlayerAttribute.Passing]);
        Assert.Equal(65, adjusted[PlayerAttribute.Speed]);
        Assert.Equal(90, adjusted.Hidden(HiddenAttribute.Professionalism));
        Assert.Equal(body, adjusted.Body);
    }

    [Fact]
    public void TwoSheetsWithTheSameContentAreEqual()
    {
        var positions = new Dictionary<Position, PositionFamiliarity> { [Position.Center] = PositionFamiliarity.Natural };
        var first = new PlayerRating(Enumerable.Repeat(55, PlayerAttributes.Count).ToArray(), Enumerable.Repeat(40, PlayerAttributes.HiddenCount).ToArray(), 60, new PlayerBody(210, 220, 115), positions, [PlayerTrait.PostsUp]);
        var second = new PlayerRating(Enumerable.Repeat(55, PlayerAttributes.Count).ToArray(), Enumerable.Repeat(40, PlayerAttributes.HiddenCount).ToArray(), 60, new PlayerBody(210, 220, 115), positions, [PlayerTrait.PostsUp]);

        Assert.Equal(first, second);
        Assert.NotEqual(first, second.With(PlayerAttribute.Blocking, 56));
    }

    [Fact]
    public void ABodyOutsideHumanRangeIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PlayerBody(260, 260, 100));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PlayerBody(200, 240, 100));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PlayerBody(200, 205, 30));
        Assert.True(PlayerBody.IsValid(200, 205, 100));
    }
}
