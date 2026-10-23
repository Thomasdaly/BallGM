using BallGM.Application.Leagues;
using BallGM.Domain.Common;
using BallGM.Domain.Contracts;
using BallGM.Domain.Negotiations;
using BallGM.Domain.Players;
using BallGM.Domain.Seasons;
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
    /// <param name="day">
    /// The season day the signing is being made on, or null where no season is under way. The one
    /// rule that reads it is this league's playoff eligibility cutoff, and a null day is reported as
    /// "not checked" rather than treated as inside the window.
    /// </param>
    DomainOperationResult<SigningAssessment> Assess(
        Offer offer,
        LeagueSnapshot snapshot,
        TeamId teamId,
        PlayerId playerId,
        SeasonDay? day = null);

    /// <inheritdoc cref="Assess"/>
    DomainOperationResult<SigningResult> Execute(
        Offer offer,
        LeagueSnapshot snapshot,
        TeamId teamId,
        PlayerId playerId,
        SeasonDay? day = null);

    /// <summary>
    /// What this league permits anyone to pay a player with this much service. Behind the port
    /// rather than computed in a read model because the ceiling is a share of the soft cap, and a
    /// screen that works that share out for itself is a screen doing cap arithmetic — the one thing
    /// the architecture boundary tests exist to keep out of the presentation layer.
    /// </summary>
    CompensationLimits LimitsFor(LeagueSnapshot snapshot, int seasonsOfService);

    /// <summary>
    /// Reads an extension offer to a player already under contract with the offering team: whether
    /// it is legal, and whether the player accepts. Never mutates.
    /// </summary>
    DomainOperationResult<ExtensionAssessment> AssessExtension(Offer offer, LeagueSnapshot snapshot);

    /// <summary>
    /// Re-assesses and, if legal and accepted, records the extension as a new contract. A refusal
    /// comes back as a success carrying the refusal and no contract — the player said no, nothing failed.
    /// </summary>
    DomainOperationResult<ExtensionResult> ExecuteExtension(Offer offer, LeagueSnapshot snapshot);

    /// <summary>
    /// Carries out an agreed buyout of <paramref name="contractId"/>: the player leaves the team, and
    /// <paramref name="retainedPercent"/> per cent of the guaranteed money still owed stays as dead money.
    /// Refused, with the reason, in a league with no buyouts or a give-back past its limit.
    /// </summary>
    DomainOperationResult ExecuteBuyout(LeagueSnapshot snapshot, TeamId teamId, PlayerId playerId, ContractId contractId, int retainedPercent);

    /// <summary>
    /// Signs a drafted player to his rookie-scale contract for round <paramref name="round"/>, pick
    /// <paramref name="selection"/>. Refused in a league with no rookie scale (the caller falls back to
    /// an ordinary signing), on a full roster, or for a player already under contract.
    /// </summary>
    DomainOperationResult<Contract> ExecuteRookieContract(LeagueSnapshot snapshot, TeamId teamId, PlayerId playerId, int round, int selection);

    /// <summary>Takes up (or declines) the option on <paramref name="season"/> of a contract, and records it.</summary>
    DomainOperationResult DecideOption(LeagueSnapshot snapshot, ContractId contractId, Domain.Leagues.Season season, bool exercise);
}

public sealed record ExtensionResult(ExtensionAssessment Assessment, Contract? Contract);

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
