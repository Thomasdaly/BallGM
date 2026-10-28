using Avalonia.Headless.XUnit;
using BallGM.Client.Avalonia.ViewModels;

namespace BallGM.Client.Avalonia.Tests;

/// <summary>
/// The person's summer offers (<c>docs/franchise-realism.md</c> → §19): made from the contracts screen
/// before the season is concluded, listed with a way to withdraw them, and reported on the season review.
/// </summary>
public sealed class SummerOfferFlowTests
{
    [AvaloniaFact]
    public void AnOfferMadeOnTheContractsScreen_StandsUntilTheSummer_AndTheReviewSaysWhatBecameOfIt()
    {
        var shell = SecondSeason();
        var season = shell.Season!;
        season.AdvanceToEndCommand.Execute(null);

        shell.SelectedSection = "Contracts";
        var contracts = Assert.IsType<ContractsViewModel>(shell.CurrentScreen);
        contracts.WholeLeague = true;
        var target = contracts.FreeAgentClasses.SelectMany(freeAgentClass => freeAgentClass.Players)
            .First(row => row.SummerOfferEligible && row.TeamName != contracts.Team!.TeamName);

        contracts.SelectForSummerCommand.Execute(target.PlayerId);
        Assert.True(contracts.HasSummerSelection);
        Assert.NotEmpty(contracts.SummerAskingLine);
        contracts.PlaceSummerOfferCommand.Execute(null);

        Assert.True(contracts.SummerOutcomeAccepted, contracts.SummerOutcome);
        var standing = Assert.Single(contracts.SummerOffers);
        Assert.Equal(target.PlayerId, standing.PlayerId);

        shell.ContinueCommand.Execute(null);

        Assert.True(season.HasReview, season.Message);
        Assert.Contains(season.Review!.YourOffers, line => line.Contains(target.FullName, StringComparison.Ordinal));
    }

    [AvaloniaFact]
    public void AnOfferCanBeWithdrawn()
    {
        var shell = SecondSeason();
        shell.SelectedSection = "Contracts";
        var contracts = Assert.IsType<ContractsViewModel>(shell.CurrentScreen);
        contracts.WholeLeague = true;
        var target = contracts.FreeAgentClasses.SelectMany(freeAgentClass => freeAgentClass.Players).First(row => row.SummerOfferEligible);

        contracts.SelectForSummerCommand.Execute(target.PlayerId);
        contracts.PlaceSummerOfferCommand.Execute(null);
        Assert.Single(contracts.SummerOffers);

        contracts.WithdrawSummerOfferCommand.Execute(target.PlayerId);

        Assert.Empty(contracts.SummerOffers);
    }

    /// <summary>
    /// The shipped league's first summer has almost nobody expiring (its contracts were all signed
    /// together); its second has a full class, so these play one season through first.
    /// </summary>
    private static MainWindowViewModel SecondSeason()
    {
        var shell = LeagueClientComposition.CreateMainWindowViewModel();
        shell.ContinueCommand.Execute(null);
        shell.Season!.AdvanceToEndCommand.Execute(null);
        shell.ContinueCommand.Execute(null);
        shell.ContinueCommand.Execute(null);
        Assert.True(shell.Season.HasSeason, shell.Season.Message);
        return shell;
    }
}
