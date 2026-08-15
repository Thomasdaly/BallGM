using BallGM.Application.Leagues;
using BallGM.Client.Avalonia.ViewModels;
using BallGM.Infrastructure.Cap;
using BallGM.Infrastructure.DraftAssets;
using BallGM.Infrastructure.Fixtures;

namespace BallGM.Client.Avalonia;

/// <summary>
/// The composition root, and the only place in the client that names a concrete
/// <see cref="ILeagueDataSource"/>. Everything under <c>Views/</c> and <c>ViewModels/</c> stays on
/// Application types; <c>ArchitectureBoundaryTests</c> enforces that, so the UI never grows a
/// direct dependency on persistence or on the ruleset file format.
/// </summary>
internal static class LeagueClientComposition
{
    public static MainWindowViewModel CreateMainWindowViewModel()
    {
        var query = new GetLeagueOverviewQuery(
            new FixtureLeagueDataSource(),
            new RulesCapLedger(),
            new RulesDraftAssetLedger());
        var result = query.Execute();

        if (result.IsFailure)
        {
            var messages = result.Errors
                .Select(error => $"{error.Code}: {error.Message}")
                .ToArray();

            return new MainWindowViewModel(messages);
        }

        return new MainWindowViewModel(result.Value);
    }
}
