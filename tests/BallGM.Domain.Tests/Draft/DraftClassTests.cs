using BallGM.Domain.Draft;
using BallGM.Domain.Leagues;
using BallGM.Domain.Players;

namespace BallGM.Domain.Tests.Draft;

public sealed class DraftClassTests
{
    [Fact]
    public void CreateSucceedsWithDistinctProspects()
    {
        var result = DraftClass.Create(
            new DraftClassId("class-2030"),
            new Season(2030),
            [MakeProspect("prospect-1"), MakeProspect("prospect-2")]);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Prospects.Count);
    }

    [Fact]
    public void CreateFailsWithNoProspects()
    {
        var result = DraftClass.Create(new DraftClassId("class-2030"), new Season(2030), []);

        Assert.True(result.IsFailure);
        Assert.Contains(result.Errors, error => error.Code == "draft_class.empty");
    }

    [Fact]
    public void CreateFailsWithADuplicateProspectId()
    {
        var result = DraftClass.Create(
            new DraftClassId("class-2030"),
            new Season(2030),
            [MakeProspect("prospect-1"), MakeProspect("prospect-1")]);

        Assert.True(result.IsFailure);
        Assert.Contains(result.Errors, error => error.Code == "draft_class.duplicate_prospect");
    }

    [Fact]
    public void FindReturnsTheMatchingProspect()
    {
        var prospect = MakeProspect("prospect-1");
        var draftClass = DraftClass.Create(new DraftClassId("class-2030"), new Season(2030), [prospect]).Value;

        Assert.Same(prospect, draftClass.Find(new ProspectId("prospect-1")));
        Assert.Null(draftClass.Find(new ProspectId("nobody")));
    }

    private static Prospect MakeProspect(string id) => Prospect.Create(
        new ProspectId(id),
        "Fictional Prospect",
        Position.Center,
        new DateOnly(2005, 7, 1),
        new PlayerRating(overall: 60)).Value;
}
