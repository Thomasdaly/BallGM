using BallGM.Domain.Common;
using BallGM.Domain.Contracts;
using BallGM.Domain.Players;
using BallGM.Domain.Teams;
using BallGM.Domain.Trades;
using BallGM.Domain.Transactions;

namespace BallGM.Rules.Trades;

/// <summary>
/// Applies a trade, or leaves the league exactly as it found it.
/// <para>
/// Execution re-validates first and refuses anything the validator will not pass, including a
/// proposal built against an older state of the league — a trade agreed five transactions ago is not
/// a trade anybody agreed to now. Nothing is applied from an assessment handed in from outside:
/// the check that matters is the one against the league as it stands at the moment of execution.
/// </para>
/// <para>
/// Every mutation pushes its inverse onto an undo stack. If a later step fails, the stack unwinds
/// and the league is byte-for-byte where it started; a half-applied trade would leave a player on
/// two rosters or a pick owned by nobody, and no ledger entry could explain it.
/// </para>
/// </summary>
public sealed class TradeExecutor
{
    private const string IllegalTradeCode = "trade.rejected";
    private const string ExecutionFailedCode = "trade.execution_rolled_back";

    private readonly TradeValidator _validator = new();

    public DomainOperationResult<TradeExecution> Execute(TradeProposal proposal, TradeContext context)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        ArgumentNullException.ThrowIfNull(context);

        var assessmentResult = _validator.Validate(proposal, context);
        if (assessmentResult.IsFailure)
        {
            return DomainOperationResult<TradeExecution>.Failure(assessmentResult.Errors.ToArray());
        }

        var assessment = assessmentResult.Value;
        if (!assessment.IsLegal)
        {
            var errors = assessment.Violations
                .Select(violation => new DomainError(violation.RuleCode, violation.Explanation))
                .Prepend(new DomainError(IllegalTradeCode, "The trade was rejected and nothing was changed."))
                .ToArray();

            return DomainOperationResult<TradeExecution>.Failure(errors);
        }

        var undo = new Stack<Action>();
        var teamsById = context.Teams.ToDictionary(team => team.Id);
        var contractsByPlayer = context.Contracts
            .Where(contract => !contract.IsTerminated)
            .ToLookup(contract => contract.PlayerId);

        var applyResult = Apply(proposal, context, teamsById, contractsByPlayer, undo);
        if (applyResult.IsFailure)
        {
            Unwind(undo);

            return DomainOperationResult<TradeExecution>.Failure(applyResult.Errors
                .Prepend(new DomainError(
                    ExecutionFailedCode,
                    "The trade could not be applied and every change made along the way was rolled back."))
                .ToArray());
        }

        var entries = RecordLedgerEntries(proposal, context, teamsById);

        return DomainOperationResult<TradeExecution>.Success(new TradeExecution(assessment, entries));
    }

    private static DomainOperationResult Apply(
        TradeProposal proposal,
        TradeContext context,
        IReadOnlyDictionary<TeamId, Team> teamsById,
        ILookup<PlayerId, Contract> contractsByPlayer,
        Stack<Action> undo)
    {
        // Rosters move as one net change per team rather than as a sequence of adds and removes:
        // a team on the roster minimum trading one-for-one is legal, and only the step-by-step
        // version of it is not.
        foreach (var teamId in proposal.Participants)
        {
            var team = teamsById[teamId];
            var outgoing = proposal.SentBy(teamId)
                .Where(movement => movement.Kind == TradeAssetKind.Player)
                .Select(movement => movement.PlayerId!)
                .ToList();

            var incoming = proposal.ReceivedBy(teamId)
                .Where(movement => movement.Kind == TradeAssetKind.Player)
                .Select(movement => movement.PlayerId!)
                .ToList();

            if (outgoing.Count == 0 && incoming.Count == 0)
            {
                continue;
            }

            var rosterBefore = team.PlayerIds.ToArray();
            var rosterResult = team.ApplyTrade(outgoing, incoming);
            if (rosterResult.IsFailure)
            {
                return rosterResult;
            }

            undo.Push(() => team.RestoreRoster(rosterBefore));
        }

        foreach (var movement in proposal.Movements)
        {
            var result = movement.Kind == TradeAssetKind.Player
                ? MoveContract(movement, contractsByPlayer, undo)
                : MovePick(movement, context, teamsById, undo);

            if (result.IsFailure)
            {
                return result;
            }
        }

        return DomainOperationResult.Success;
    }

    private static DomainOperationResult MoveContract(
        TradeAssetMovement movement,
        ILookup<PlayerId, Contract> contractsByPlayer,
        Stack<Action> undo)
    {
        var contract = contractsByPlayer[movement.PlayerId!]
            .FirstOrDefault(candidate => candidate.TeamId == movement.FromTeamId);

        if (contract is null)
        {
            return DomainOperationResult.Failure(new DomainError(
                "trade.player_has_no_contract",
                $"Player '{movement.PlayerId!.Value}' has no live contract with team '{movement.FromTeamId.Value}'."));
        }

        var previousTeam = contract.TeamId;
        var result = contract.TransferTo(movement.ToTeamId);
        if (result.IsFailure)
        {
            return result;
        }

        undo.Push(() => contract.TransferTo(previousTeam));
        return DomainOperationResult.Success;
    }

    private static DomainOperationResult MovePick(
        TradeAssetMovement movement,
        TradeContext context,
        IReadOnlyDictionary<TeamId, Team> teamsById,
        Stack<Action> undo)
    {
        var pickId = movement.DraftPickId!;
        var ownership = context.DraftAssets.Ownership(pickId);
        if (ownership is null)
        {
            return DomainOperationResult.Failure(new DomainError(
                "trade.unknown_pick",
                $"Pick '{pickId.Value}' is not a registered draft asset in this league."));
        }

        var previousOwner = ownership.CurrentOwnerFranchiseId;
        var result = context.DraftAssets.Transfer(pickId, teamsById[movement.ToTeamId].FranchiseId);
        if (result.IsFailure)
        {
            return result;
        }

        undo.Push(() => context.DraftAssets.Transfer(pickId, previousOwner));
        return DomainOperationResult.Success;
    }

    /// <summary>
    /// Writes the trade into the same ledger every other state change goes through — one line per
    /// asset, naming both ends, so a cap sheet and a pick board can each explain themselves from it.
    /// Recorded only after the trade is fully applied: a ledger line for a change that did not
    /// happen is worse than no line at all.
    /// </summary>
    private static IReadOnlyList<TransactionEntry> RecordLedgerEntries(
        TradeProposal proposal,
        TradeContext context,
        IReadOnlyDictionary<TeamId, Team> teamsById)
    {
        var entries = new List<TransactionEntry>(proposal.Movements.Count);

        foreach (var movement in proposal.Movements)
        {
            var from = teamsById[movement.FromTeamId];
            var to = teamsById[movement.ToTeamId];

            if (movement.Kind == TradeAssetKind.Player)
            {
                var player = context.Players.FirstOrDefault(candidate => candidate.Id == movement.PlayerId);
                var contract = context.Contracts.FirstOrDefault(candidate =>
                    candidate.PlayerId == movement.PlayerId && candidate.TeamId == to.Id && !candidate.IsTerminated);

                entries.Add(context.Ledger.Record(
                    TransactionKind.PlayerTraded,
                    context.CurrentSeason,
                    from.Id,
                    $"{player?.FullName ?? movement.PlayerId!.Value} was traded to {to.Name}.",
                    movement.PlayerId,
                    contract?.Id,
                    contract?.ChargeFor(context.CurrentSeason)?.Amount));

                entries.Add(context.Ledger.Record(
                    TransactionKind.PlayerTraded,
                    context.CurrentSeason,
                    to.Id,
                    $"{player?.FullName ?? movement.PlayerId!.Value} arrived from {from.Name}.",
                    movement.PlayerId,
                    contract?.Id,
                    contract?.ChargeFor(context.CurrentSeason)?.Amount));

                continue;
            }

            var pick = context.DraftAssets.Pick(movement.DraftPickId!);
            var description = pick is null
                ? $"Pick '{movement.DraftPickId!.Value}'"
                : $"The {pick.DraftSeason.Year} round {pick.Round} pick";

            entries.Add(context.Ledger.RecordPickEvent(
                TransactionKind.DraftPickTransferred,
                context.CurrentSeason,
                from.FranchiseId,
                movement.DraftPickId!,
                $"{description} went to {to.Name} in a trade.",
                to.FranchiseId));
        }

        return entries;
    }

    private static void Unwind(Stack<Action> undo)
    {
        while (undo.Count > 0)
        {
            undo.Pop()();
        }
    }
}

/// <summary>What an executed trade did: the assessment it passed, and the ledger lines it left behind.</summary>
public sealed record TradeExecution(TradeAssessment Assessment, IReadOnlyList<TransactionEntry> LedgerEntries);
