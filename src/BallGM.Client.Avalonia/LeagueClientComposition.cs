using BallGM.Application.Leagues;
using BallGM.Client.Avalonia.ViewModels;
using BallGM.Infrastructure.Cap;
using BallGM.Infrastructure.DraftAssets;
using BallGM.Infrastructure.Fixtures;
using BallGM.Infrastructure.Trades;

namespace BallGM.Client.Avalonia;

/// <summary>
/// The composition root, and the only place in the client that names a concrete
/// <see cref="ILeagueDataSource"/>. Everything under <c>Views/</c> and <c>ViewModels/</c> stays on
/// Application types; <c>ArchitectureBoundaryTests</c> enforces that, so the UI never grows a
/// direct dependency on persistence or on the ruleset file format.
/// <para>
/// The session is created here and lives for the run. Before trades existed, every screen could
/// reload the league on demand; now that a trade changes it, one owner has to hold it.
/// </para>
/// </summary>
internal static class LeagueClientComposition
{
    public static MainWindowViewModel CreateMainWindowViewModel()
    {
        var session = new LeagueSession(
            new FixtureLeagueDataSource(),
            new RulesCapLedger(),
            new RulesDraftAssetLedger(),
            new RulesTradeEngine());

        var result = session.Load();

        if (result.IsFailure)
        {
            var messages = result.Errors
                .Select(error => $"{error.Code}: {error.Message}")
                .ToArray();

            return new MainWindowViewModel(messages);
        }

        return new MainWindowViewModel(result.Value, session);
    }
}
