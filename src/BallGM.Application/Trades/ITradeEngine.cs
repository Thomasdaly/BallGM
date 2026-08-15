using BallGM.Application.Leagues;
using BallGM.Domain.Common;
using BallGM.Domain.Trades;

namespace BallGM.Application.Trades;

/// <summary>
/// The port an Application use case reaches the trade rules through — the same arrangement as
/// <see cref="Cap.ICapLedger"/> and <see cref="DraftAssets.IDraftAssetLedger"/>, because Application
/// still does not reference Rules.
/// <para>
/// Two operations, deliberately separate. <see cref="Assess"/> never touches league state, so a GM
/// can rework a proposal as many times as they like; <see cref="Execute"/> re-checks everything
/// against the league as it stands and either applies the whole trade or changes nothing.
/// </para>
/// </summary>
public interface ITradeEngine
{
    DomainOperationResult<TradeAssessment> Assess(TradeProposal proposal, LeagueSnapshot snapshot);

    DomainOperationResult<TradeResult> Execute(TradeProposal proposal, LeagueSnapshot snapshot);
}

/// <summary>
/// What an executed trade did, in Application terms: the assessment it passed and how many ledger
/// lines it left. The entries themselves stay in the ledger, where every other consumer reads them.
/// </summary>
public sealed record TradeResult(TradeAssessment Assessment, int LedgerEntryCount);
