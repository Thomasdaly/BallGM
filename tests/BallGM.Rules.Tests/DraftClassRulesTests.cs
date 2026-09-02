using BallGM.Rules.Configuration;

namespace BallGM.Rules.Tests;

public sealed class DraftClassRulesTests
{
    [Fact]
    public void CreateSucceedsWithValidBounds()
    {
        var result = DraftClassRules.Create(classSize: 60, minimumRating: 30, maximumRating: 90, prospectAgeYears: 19);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.IsConfigured);
        Assert.Equal(60, result.Value.ClassSize);
    }

    [Fact]
    public void NoneIsNotConfigured()
    {
        Assert.False(DraftClassRules.None.IsConfigured);
    }

    [Fact]
    public void CreateRejectsANonPositiveClassSize()
    {
        var result = DraftClassRules.Create(classSize: 0, minimumRating: 30, maximumRating: 90, prospectAgeYears: 19);

        Assert.True(result.IsFailure);
        Assert.Contains(result.Errors, error => error.Code == "ruleset.non_positive_draft_class_size");
    }

    [Fact]
    public void CreateRejectsInvertedRatingBounds()
    {
        var result = DraftClassRules.Create(classSize: 60, minimumRating: 90, maximumRating: 30, prospectAgeYears: 19);

        Assert.True(result.IsFailure);
        Assert.Contains(result.Errors, error => error.Code == "ruleset.inverted_draft_class_rating_bounds");
    }

    [Theory]
    [InlineData(-1, 90)]
    [InlineData(30, 101)]
    public void CreateRejectsARatingBoundOutsideTheRatingScale(int minimum, int maximum)
    {
        var result = DraftClassRules.Create(classSize: 60, minimumRating: minimum, maximumRating: maximum, prospectAgeYears: 19);

        Assert.True(result.IsFailure);
        Assert.Contains(result.Errors, error => error.Code == "ruleset.invalid_draft_class_rating_bound");
    }

    [Fact]
    public void CreateRejectsANonPositiveProspectAge()
    {
        var result = DraftClassRules.Create(classSize: 60, minimumRating: 30, maximumRating: 90, prospectAgeYears: 0);

        Assert.True(result.IsFailure);
        Assert.Contains(result.Errors, error => error.Code == "ruleset.non_positive_prospect_age");
    }
}
