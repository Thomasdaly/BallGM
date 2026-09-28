using BallGM.Application.Leagues;
using BallGM.Client.Avalonia.ViewModels;
using BallGM.Infrastructure.AI;
using BallGM.Infrastructure.Cap;
using BallGM.Infrastructure.DraftAssets;
using BallGM.Infrastructure.Fixtures;
using BallGM.Infrastructure.LeaguePacks;
using BallGM.Infrastructure.Negotiations;
using BallGM.Infrastructure.Saves;
using BallGM.Infrastructure.Seasons;
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
    /// <summary>
    /// Names a league pack file to open instead of the shipped fixture league. Unset, the client
    /// opens the fixture exactly as before.
    /// </summary>
    public const string LeaguePackEnvironmentVariable = "BALLGM_LEAGUE_PACK";

    public static MainWindowViewModel CreateMainWindowViewModel()
    {
        var packPath = Environment.GetEnvironmentVariable(LeaguePackEnvironmentVariable);
        ILeagueDataSource dataSource = string.IsNullOrWhiteSpace(packPath)
            ? new FixtureLeagueDataSource()
            : new LeaguePackDataSource(packPath);

        var session = new LeagueSession(
            dataSource,
            new RulesCapLedger(),
            new RulesDraftAssetLedger(),
            new RulesTradeEngine(),
            new RulesSigningEngine(),
            new RulesFreeAgencyMarket(),
            new RulesSeasonEngine(),
            new SaveGameSerializer(),
            new RulesFrontOfficeAdvisor(new RulesCapLedger()));

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
