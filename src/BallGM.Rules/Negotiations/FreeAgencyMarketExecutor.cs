using BallGM.Domain.Common;
using BallGM.Domain.Negotiations;
using BallGM.Domain.Transactions;
using BallGM.Rules.Signings;

namespace BallGM.Rules.Negotiations;

/// <summary>
/// Resolves a market for real: expires what has timed out, records the player's decision, and signs
/// the winning offer — or leaves the negotiation exactly as it found it.
/// <para>
/// The same shape as <c>TradeExecutor</c> and <see cref="SigningExecutor"/>, and re-validating for
/// the same reason: the assessment that matters is the one against the league as it stands at the
/// moment of resolution, never one handed in from outside. Two teams can both have been able to
/// afford this player when they bid and only one of them still be able to now.
/// </para>
/// <para>
/// Order matters here. Every mutation of the negotiation happens first and is fully reversible
/// through <see cref="Negotiation.RestoreTo"/>; the signing — which creates a contract and puts a
/// player on a roster — happens last, so a refusal at that point unwinds a history and not a league.
/// </para>
/// </summary>
public sealed class FreeAgencyMarketExecutor
{
    public const string RolledBackCode = "market.resolution_rolled_back";
    public const string SignedButNotRecordedCode = "market.signed_but_not_recorded";

    private readonly FreeAgencyMarketResolver _resolver = new();
    private readonly SigningExecutor _signingExecutor = new();

    public DomainOperationResult<MarketExecution> Resolve(Negotiation negotiation, MarketContext context)
    {
        ArgumentNullException.ThrowIfNull(negotiation);
        ArgumentNullException.ThrowIfNull(context);

        var assessmentResult = _resolver.Assess(negotiation, context);
        if (assessmentResult.IsFailure)
        {
            return DomainOperationResult<MarketExecution>.Failure(assessmentResult.Errors.ToArray());
        }

        var assessment = assessmentResult.Value;

        var restoreState = negotiation.State;
        var restoreAccepted = negotiation.AcceptedOfferId;
        var restoreContract = negotiation.SignedContractId;
        var restoreHistoryCount = negotiation.History.Count;

        void Unwind() => negotiation.RestoreTo(restoreState, restoreAccepted, restoreContract, restoreHistoryCount);

        foreach (var expired in assessment.ExpiringOffers)
        {
            var expiryResult = negotiation.RecordExpiry(expired.Id, context.Day);
            if (expiryResult.IsFailure)
            {
                Unwind();
                return Rolled(expiryResult.Errors, "An offer could not be recorded as expired");
            }
        }

        var resolveResult = negotiation.Resolve(assessment.AcceptedOfferId, context.Day, assessment.Narrative);
        if (resolveResult.IsFailure)
        {
            Unwind();
            return Rolled(resolveResult.Errors, "The market's outcome could not be recorded");
        }

        if (assessment.AcceptedOfferId is null)
        {
            // A market that resolves on nobody is an outcome, not a failure: the negotiation is closed,
            // no contract exists, and the ledger has nothing to say because nothing changed hands.
            return DomainOperationResult<MarketExecution>.Success(
                new MarketExecution(assessment, null, null, Array.Empty<TransactionEntry>()));
        }

        var accepted = assessment.Winner!.Offer;

        var team = context.TeamFor(accepted.TeamId);
        if (team is null)
        {
            Unwind();
            return Rolled(
                [new DomainError(FreeAgencyMarketResolver.UnknownTeamCode, $"Team '{accepted.TeamId.Value}' is not in this league.")],
                "The winning team left the league between assessment and execution");
        }

        var signingResult = _signingExecutor.Execute(accepted, context.SigningContextFor(team));
        if (signingResult.IsFailure)
        {
            Unwind();
            return Rolled(signingResult.Errors, "The accepted offer could not be signed");
        }

        var signing = signingResult.Value;

        var recordResult = negotiation.RecordSigned(signing.Contract.Id, context.Day);
        if (recordResult.IsFailure)
        {
            // Unreachable by construction: the negotiation was put into Resolved on this very day a
            // few lines above, which is exactly what RecordSigned requires. It is handled rather than
            // asserted because the alternative to an honest message here is a silent lie in a save —
            // and deliberately without an unwind, because the contract now exists and a history that
            // denied it would be the worse of the two wrong states.
            return DomainOperationResult<MarketExecution>.Failure(recordResult.Errors
                .Prepend(new DomainError(
                    SignedButNotRecordedCode,
                    $"{context.Player.FullName} was signed and the contract exists, but the negotiation could not record it."))
                .ToArray());
        }

        return DomainOperationResult<MarketExecution>.Success(new MarketExecution(
            assessment,
            signing.Contract,
            signing.Route,
            signing.LedgerEntries));
    }

    private static DomainOperationResult<MarketExecution> Rolled(IReadOnlyList<DomainError> errors, string preamble) =>
        DomainOperationResult<MarketExecution>.Failure(errors
            .Prepend(new DomainError(RolledBackCode, $"{preamble}, and the negotiation was left exactly as it was."))
            .ToArray());
}
