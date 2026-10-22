using BallGM.Application.Leagues;
using BallGM.Client.Avalonia.Controls;
using BallGM.Client.Avalonia.Diagnostics;
using BallGM.Client.Avalonia.Imaging;
using BallGM.Client.Avalonia.ViewModels;
using BallGM.Infrastructure.AI;
using BallGM.Infrastructure.Cap;
using BallGM.Infrastructure.DraftAssets;
using BallGM.Infrastructure.Fixtures;
using BallGM.Infrastructure.LeaguePacks;
using BallGM.Infrastructure.Negotiations;
using BallGM.Infrastructure.Packs;
using BallGM.Infrastructure.Saves;
using BallGM.Infrastructure.Seasons;
using BallGM.Infrastructure.Trades;
using BallGM.Mods.Checking;
using BallGM.Mods.Library;
using BallGM.Mods.Loading;
using BallGM.Mods.Setups;

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

    /// <summary>
    /// Names a league setup — a setup file, or the id of an installed setup pack such as
    /// <c>builtin.setup.na-major-fictional</c> — to check with the pack checker and open. Takes
    /// precedence over <see cref="LeaguePackEnvironmentVariable"/>. A setup the checker blocks is not
    /// opened; the window shows the checker's report instead.
    /// </summary>
    public const string LeagueSetupEnvironmentVariable = "BALLGM_LEAGUE_SETUP";

    public static MainWindowViewModel CreateMainWindowViewModel()
    {
        var packPath = Environment.GetEnvironmentVariable(LeaguePackEnvironmentVariable);
        ILeagueDataSource dataSource = string.IsNullOrWhiteSpace(packPath)
            ? new FixtureLeagueDataSource()
            : new LeaguePackDataSource(packPath);

        var setupName = Environment.GetEnvironmentVariable(LeagueSetupEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(setupName))
        {
            var checkedSetup = CheckSetup(setupName);
            if (checkedSetup.Refusal is not null)
            {
                return new MainWindowViewModel(checkedSetup.Refusal);
            }

            dataSource = checkedSetup.DataSource!;
        }

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

        // Every image in the UI resolves through one service, built on the loaded pack's own mapping.
        UiServices.Assets = new AssetResolver(() => session.Overview() is { IsSuccess: true } overview
            ? AssetCatalogBuilder.Build(overview.Value, session.Artwork, string.IsNullOrWhiteSpace(packPath) ? null : packPath)
            : AssetCatalog.Empty,
            log: UiLog.Warn);

        if (result.IsFailure)
        {
            var messages = result.Errors
                .Select(error => $"{error.Code}: {error.Message}")
                .ToArray();

            return new MainWindowViewModel(messages);
        }

        return new MainWindowViewModel(result.Value, session);
    }

    /// <summary>
    /// Runs the pack checker over the named setup. Blocked: the report, as the lines the window shows
    /// in place of a league. Otherwise the league exactly as checked, cosmetics and all.
    /// </summary>
    private static (ILeagueDataSource? DataSource, string[]? Refusal) CheckSetup(string setupName)
    {
        var library = PackLibrary.Scan(PackFolders.Default());
        var setup = SetupLocator.Locate(setupName, library);
        if (setup.IsFailure)
        {
            return (null, setup.Errors.Select(error => $"{error.Code}: {error.Message}").ToArray());
        }

        var probe = new PackContentProbe();
        var report = new PackChecker(probe).Check(setup.Value, library);
        return report.Verdict == PackCheckVerdict.Blocked
            ? (null, PackCheckReportText.Format(report, includeInfo: false).Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries))
            : (new CheckedLeagueDataSource(report, probe), null);
    }
}
