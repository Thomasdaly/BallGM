using BallGM.Domain.Common;
using BallGM.Domain.Contracts;
using BallGM.Domain.Negotiations;
using BallGM.Domain.Transactions;

namespace BallGM.Rules.Signings;

/// <summary>
/// Signs a player, or leaves the league exactly as it found it.
/// <para>
/// The same shape as <c>TradeExecutor</c>, and for the same reasons. Execution re-validates rather
/// than trusting an assessment handed in from outside — the check that matters is the one against
/// the league as it stands at the moment of signing, not the one the offer screen ran a minute ago.
/// Every mutation pushes its inverse onto an undo stack, so a failure halfway through leaves no
/// player on a roster without a contract and no contract naming a player nobody rostered.
/// </para>
/// <para>
/// Ledger entries are written last, after every mutation has succeeded. An entry recorded and then
/// rolled back would be a line in an audit trail describing something that did not happen, which is
/// worse than no line at all.
/// </para>
/// </summary>
public sealed class SigningExecutor
{
    public const string RejectedCode = "signing.rejected";
    public const string RolledBackCode = "signing.execution_rolled_back";

    private readonly SigningValidator _validator = new();

    public DomainOperationResult<SigningExecution> Execute(Offer offer, SigningContext context)
    {
        ArgumentNullException.ThrowIfNull(offer);
        ArgumentNullException.ThrowIfNull(context);

        var assessmentResult = _validator.Validate(offer, context);
        if (assessmentResult.IsFailure)
        {
            return DomainOperationResult<SigningExecution>.Failure(assessmentResult.Errors.ToArray());
        }

        var assessment = assessmentResult.Value;
        if (!assessment.IsLegal)
        {
            var errors = assessment.Violations
                .Select(violation => new DomainError(violation.RuleCode, violation.Explanation))
                .Prepend(new DomainError(RejectedCode, "The signing was refused and nothing was changed."))
                .ToArray();

            return DomainOperationResult<SigningExecution>.Failure(errors);
        }

        // IsLegal guarantees this: a signing with no permitting route carries a violation, so a legal
        // assessment always has one.
        var route = assessment.PermittingRoute!.Kind;

        var contractResult = Contract.Create(
            new ContractId(SortableId.NewId()),
            context.Team.Id,
            context.Player.Id,
            offer.ToContractTerms());

        if (contractResult.IsFailure)
        {
            return DomainOperationResult<SigningExecution>.Failure(contractResult.Errors
                .Prepend(new DomainError(
                    RolledBackCode,
                    "The offer could not be turned into a contract and nothing was changed."))
                .ToArray());
        }

        var undo = new Stack<Action>();

        var rosterBefore = context.Team.PlayerIds.ToArray();
        var rosterResult = context.Team.AddPlayer(context.Player.Id);
        if (rosterResult.IsFailure)
        {
            Unwind(undo);

            return DomainOperationResult<SigningExecution>.Failure(rosterResult.Errors
                .Prepend(new DomainError(
                    RolledBackCode,
                    "The player could not be added to the roster and every change made along the way was rolled back."))
                .ToArray());
        }

        // RestoreRoster rather than RemovePlayer: an undo has to put the roster back exactly as it
        // was, and RemovePlayer is a rule-checked operation that can legitimately refuse — a team
        // signing its way up to the roster minimum could not undo the signing that got it there.
        undo.Push(() => context.Team.RestoreRoster(rosterBefore));

        var entries = new List<TransactionEntry>
        {
            context.Ledger.RecordSigning(
                context.CurrentSeason,
                context.Team.Id,
                context.Player.Id,
                contractResult.Value.Id,
                offer.FirstSeasonCompensation,
                route,
                $"{context.Player.FullName} signed with {context.Team.Name} for {offer.SeasonCount} season(s), {assessment.PermittingRoute.Explanation}"),
        };

        return DomainOperationResult<SigningExecution>.Success(
            new SigningExecution(assessment, contractResult.Value, route, entries));
    }

    private static void Unwind(Stack<Action> undo)
    {
        while (undo.Count > 0)
        {
            undo.Pop()();
        }
    }
}
