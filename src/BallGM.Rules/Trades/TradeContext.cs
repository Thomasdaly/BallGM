using BallGM.Domain.Contracts;
using BallGM.Domain.DraftAssets;
using BallGM.Domain.Leagues;
using BallGM.Domain.Players;
using BallGM.Domain.Teams;
using BallGM.Domain.Transactions;
using BallGM.Rules.Configuration;

namespace BallGM.Rules.Trades;

/// <summary>
/// Everything the trade rules need to judge a proposal: the league's current aggregates and the
/// configured rules to judge them against. Assembled by the caller — <c>BallGM.Infrastructure</c>
/// maps a loaded league onto this — so the rules layer never loads anything itself.
/// </summary>
public sealed record TradeContext(
    Season CurrentSeason,
    IReadOnlyCollection<Team> Teams,
    IReadOnlyCollection<Player> Players,
    IReadOnlyCollection<Contract> Contracts,
    DraftAssetBook DraftAssets,
    TransactionLedger Ledger,
    RosterSizeLimits RosterLimits,
    CapThresholds CapThresholds,
    TradeRules TradeRules,
    DraftRules DraftRules);
