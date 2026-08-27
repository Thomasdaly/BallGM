using BallGM.Application.Leagues;
using BallGM.Domain.Common;
using BallGM.Domain.Contracts;
using BallGM.Domain.Negotiations;
using BallGM.Domain.Players;
using BallGM.Domain.Teams;

namespace BallGM.Application.Negotiations;

/// <summary>
/// The port an Application use case reaches the signing rules through — the same arrangement as
/// <see cref="Trades.ITradeEngine"/> and <see cref="Cap.ICapLedger"/>, because Application still does
/// not reference Rules.
/// <para>
/// Two operations, deliberately separate, exactly as the trade engine has. <see cref="Assess"/> never
/// touches league state, so an offer screen can rework a proposal on every keystroke;
/// <see cref="Execute"/> re-checks everything against the league as it stands and either signs the
/// player or changes nothing.
/// </para>
/// </summary>
public interface ISigningEngine
{
    DomainOperationResult<SigningAssessment> Assess(
        Offer offer,
        LeagueSnapshot snapshot,
        TeamId teamId,
        PlayerId playerId);

    DomainOperationResult<SigningResult> Execute(
        Offer offer,
        LeagueSnapshot snapshot,
        TeamId teamId,
        PlayerId playerId);

    /// <summary>
    /// What this league permits anyone to pay a player with this much service. Behind the port
    /// rather than computed in a read model because the ceiling is a share of the soft cap, and a
    /// screen that works that share out for itself is a screen doing cap arithmetic — the one thing
    /// the architecture boundary tests exist to keep out of the presentation layer.
    /// </summary>
    CompensationLimits LimitsFor(LeagueSnapshot snapshot, int seasonsOfService);
}

/// <summary>
/// The floor and ceiling for one player's service. Either may be <c>null</c>, meaning the league
/// configures no such line — not that the line is nought.
/// </summary>
public sealed record CompensationLimits(Money? Minimum, Money? Maximum);

/// <summary>
/// What an executed signing did, in Application terms. The contract travels back because the session
/// has to add it to the league it holds — a signing is the one transaction that creates an aggregate
/// rather than moving one that already existed.
/// </summary>
public sealed record SigningResult(
    SigningAssessment Assessment,
    Contract Contract,
    SigningRouteKind Route,
    int LedgerEntryCount);
