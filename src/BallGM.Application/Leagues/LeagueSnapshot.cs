using BallGM.Domain.Common;
using BallGM.Domain.Contracts;
using BallGM.Domain.DraftAssets;
using BallGM.Domain.Franchises;
using BallGM.Domain.Leagues;
using BallGM.Domain.Players;
using BallGM.Domain.Teams;
using BallGM.Domain.Transactions;

namespace BallGM.Application.Leagues;

/// <summary>
/// The loaded aggregates and configuration one league needs, as handed to the Application layer
/// by an <see cref="ILeagueDataSource"/>. This is an input model, not a read model: it still
/// carries Domain aggregates, because mapping them to the presentation-facing
/// <see cref="LeagueOverview"/> is <see cref="GetLeagueOverviewQuery"/>'s job.
/// </summary>
/// <remarks>
/// <see cref="Players"/> can legitimately include people no team's roster references — a released
/// player whose guaranteed money is still on the books is exactly that case, and the cap sheet
/// needs their name to explain the dead-money line.
/// </remarks>
public sealed record LeagueSnapshot(
    League League,
    Season CurrentSeason,
    IReadOnlyCollection<Franchise> Franchises,
    IReadOnlyCollection<Team> Teams,
    IReadOnlyCollection<Player> Players,
    IReadOnlyCollection<Contract> Contracts,
    DraftAssetBook DraftAssets,
    TransactionLedger Ledger,
    LeagueConfiguration Configuration);

/// <summary>
/// The subset of a league's configured ruleset the Application layer needs, expressed in Domain
/// value objects. Deliberately not <c>BallGM.Rules.Configuration.LeagueRuleset</c>: Application
/// does not reference Rules, so whoever implements <see cref="ILeagueDataSource"/> owns the
/// mapping from the on-disk ruleset onto this shape.
/// </summary>
public sealed record LeagueConfiguration(
    string RulesetName,
    int RegularSeasonGameCount,
    RosterSizeLimits RosterLimits,
    Money SoftCap,
    Money LuxuryTax,
    Money FirstApron,
    Money SecondApron,
    Money HardCap,
    int DraftRoundCount,
    bool DraftLotteryEnabled,
    int TradableFutureDraftHorizon,
    int RetainedRoundNumber,
    int RetainedRoundInterval);
