using BallGM.Domain.Draft;
using BallGM.Domain.Leagues;
using BallGM.Domain.Randomness;
using BallGM.Rules.Configuration;
using BallGM.Rules.Draft;

namespace BallGM.Rules.Tests.Draft;

public sealed class ProspectGeneratorTests
{
    [Fact]
    public void GenerateProducesTheConfiguredClassSizeWithinRatingBounds()
    {
        var rules = DraftClassRules.Create(classSize: 40, minimumRating: 30, maximumRating: 85, prospectAgeYears: 19).Value;

        var result = ProspectGenerator.Generate(
            new DraftClassId("class-2030"),
            new Season(2030),
            rules,
            new SeededRandomSource(seed: 1));

        Assert.True(result.IsSuccess);
        Assert.Equal(40, result.Value.Prospects.Count);
        Assert.All(result.Value.Prospects, prospect =>
        {
            Assert.InRange(prospect.TrueRating.Overall, 30, 85);
            Assert.Equal(2030 - 19, prospect.BirthDate.Year);
        });
    }

    [Fact]
    public void GenerateFailsWhenClassRulesAreNotConfigured()
    {
        var result = ProspectGenerator.Generate(
            new DraftClassId("class-2030"),
            new Season(2030),
            DraftClassRules.None,
            new SeededRandomSource(seed: 1));

        Assert.True(result.IsFailure);
        Assert.Contains(result.Errors, error => error.Code == "draft_class.generator_not_configured");
    }

    [Fact]
    public void GenerateIsDeterministicForTheSameSeed()
    {
        var rules = DraftClassRules.Create(classSize: 20, minimumRating: 30, maximumRating: 85, prospectAgeYears: 19).Value;

        var first = ProspectGenerator.Generate(new DraftClassId("class-2030"), new Season(2030), rules, new SeededRandomSource(seed: 42));
        var second = ProspectGenerator.Generate(new DraftClassId("class-2030"), new Season(2030), rules, new SeededRandomSource(seed: 42));

        var firstRatings = first.Value.Prospects.Select(prospect => prospect.TrueRating.Overall).ToArray();
        var secondRatings = second.Value.Prospects.Select(prospect => prospect.TrueRating.Overall).ToArray();
        var firstNames = first.Value.Prospects.Select(prospect => prospect.FullName).ToArray();
        var secondNames = second.Value.Prospects.Select(prospect => prospect.FullName).ToArray();

        Assert.Equal(firstRatings, secondRatings);
        Assert.Equal(firstNames, secondNames);
    }

    [Fact]
    public void GenerateSpreadsPositionsRoundRobin()
    {
        var rules = DraftClassRules.Create(classSize: 10, minimumRating: 30, maximumRating: 85, prospectAgeYears: 19).Value;

        var result = ProspectGenerator.Generate(new DraftClassId("class-2030"), new Season(2030), rules, new SeededRandomSource(seed: 7));

        var positionCounts = result.Value.Prospects
            .GroupBy(prospect => prospect.Position)
            .ToDictionary(group => group.Key, group => group.Count());

        Assert.Equal(5, positionCounts.Count);
        Assert.All(positionCounts.Values, count => Assert.Equal(2, count));
    }
}
