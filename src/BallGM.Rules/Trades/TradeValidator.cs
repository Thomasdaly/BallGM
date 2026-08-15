using BallGM.Domain.Cap;
using BallGM.Domain.Common;
using BallGM.Domain.Contracts;
using BallGM.Domain.DraftAssets;
using BallGM.Domain.Franchises;
using BallGM.Domain.Players;
using BallGM.Domain.Teams;
using BallGM.Domain.Trades;
using BallGM.Rules.Cap;
using BallGM.Rules.DraftAssets;

namespace BallGM.Rules.Trades;

/// <summary>
/// Judges a trade without touching the league. Everything it returns — blocking violations,
/// non-blocking warnings, and the resulting payroll and roster for every team involved — is computed
/// from projections, so the same proposal can be assessed a hundred times while a GM tinkers with it
/// and the league is exactly where it started afterwards.
/// <para>
/// It owns no rules of its own where a rule already exists: pick ownership goes through
/// <see cref="PickOwnershipRules"/> and threshold standing through <see cref="CapLedger"/>, so a
/// trade cannot legalise something the pick board or the cap sheet would call illegal.
/// </para>
/// </summary>
public sealed class TradeValidator
{
    private const string UnknownTeamCode = "trade.unknown_team";
    private const string StaleProposalCode = "trade.stale_proposal";
    private const string UnknownPlayerCode = "trade.unknown_player";
    private const string PlayerNotOnTeamCode = "trade.player_not_on_team";
    private const string NoContractCode = "trade.player_has_no_contract";
    private const string InjuredBlockedCode = "trade.injured_player_not_tradeable";
    private const string InjuredWarningCode = "trade.injured_player_traded";
    private const string RosterMaximumCode = "trade.roster_maximum_exceeded";
    private const string RosterMinimumCode = "trade.roster_minimum_not_met";
    private const string SalaryMatchCode = "trade.salary_not_matched";
    private const string HardCapCode = "trade.hard_cap_exceeded";
    private const string SecondApronCode = "trade.second_apron_salary_increase";
    private const string CrossesTaxCode = "trade.crosses_luxury_tax";
    private const string CrossesApronCode = "trade.crosses_apron";

    private readonly CapLedger _capLedger = new();
    private readonly PickOwnershipRules _pickRules = new();

    /// <summary>
    /// Assesses a proposal. A structural failure — a participant that is not a team in this league —
    /// comes back as a failed result, because there is nothing coherent to assess. Everything a GM
    /// could plausibly propose comes back as a successful assessment carrying violations.
    /// </summary>
    public DomainOperationResult<TradeAssessment> Validate(TradeProposal proposal, TradeContext context)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        ArgumentNullException.ThrowIfNull(context);

        var teamsById = context.Teams.ToDictionary(team => team.Id);
        var missingTeams = proposal.Participants.Where(id => !teamsById.ContainsKey(id)).ToList();
        if (missingTeams.Count > 0)
        {
            return DomainOperationResult<TradeAssessment>.Failure(missingTeams
                .Select(id => new DomainError(UnknownTeamCode, $"Team '{id.Value}' is not a team in this league."))
                .ToArray());
        }

        var playersById = context.Players.ToDictionary(player => player.Id);
        var violations = new List<TradeRuleFinding>();
        var warnings = new List<TradeRuleFinding>();

        // A proposal built against a league that has since moved cannot be trusted: the player in it
        // may already have been traded, the pick may already have conveyed.
        if (proposal.IsStaleAgainst(context.Ledger))
        {
            violations.Add(new TradeRuleFinding(
                StaleProposalCode,
                "The league has changed since this trade was put together. Rebuild the proposal against the current rosters before submitting it."));
        }

        var movedContracts = new Dictionary<string, Contract>(StringComparer.Ordinal);

        foreach (var movement in proposal.Movements)
        {
            if (movement.Kind == TradeAssetKind.Player)
            {
                ValidatePlayerMovement(movement, context, teamsById, playersById, movedContracts, violations, warnings);
            }
            else
            {
                ValidatePickMovement(movement, context, teamsById, violations);
            }
        }

        var outcomes = new List<TradeTeamOutcome>();

        foreach (var teamId in proposal.Participants)
        {
            var outcomeResult = BuildOutcome(proposal, context, teamsById[teamId], movedContracts);
            if (outcomeResult.IsFailure)
            {
                return DomainOperationResult<TradeAssessment>.Failure(outcomeResult.Errors.ToArray());
            }

            var outcome = outcomeResult.Value;
            outcomes.Add(outcome);

            CheckRosterLimits(outcome, context, violations);
            CheckMoney(outcome, context, violations, warnings);
        }

        return DomainOperationResult<TradeAssessment>.Success(
            new TradeAssessment(proposal.Id, violations, warnings, outcomes));
    }

    private void ValidatePlayerMovement(
        TradeAssetMovement movement,
        TradeContext context,
        IReadOnlyDictionary<TeamId, Team> teamsById,
        IReadOnlyDictionary<PlayerId, Player> playersById,
        IDictionary<string, Contract> movedContracts,
        List<TradeRuleFinding> violations,
        List<TradeRuleFinding> warnings)
    {
        var playerId = movement.PlayerId!;
        var fromTeam = teamsById[movement.FromTeamId];

        if (!playersById.TryGetValue(playerId, out var player))
        {
            violations.Add(new TradeRuleFinding(
                UnknownPlayerCode,
                $"Player '{playerId.Value}' is not a player in this league.",
                movement.FromTeamId));
            return;
        }

        if (!fromTeam.PlayerIds.Contains(playerId))
        {
            violations.Add(new TradeRuleFinding(
                PlayerNotOnTeamCode,
                $"{player.FullName} is not on {fromTeam.Name}'s roster and cannot be traded by them.",
                movement.FromTeamId));
            return;
        }

        // Salary has to travel with the player, so a player with nothing to travel is a hole in the
        // league's own data rather than something to wave through.
        var contract = context.Contracts.FirstOrDefault(candidate =>
            candidate.PlayerId == playerId && candidate.TeamId == fromTeam.Id && !candidate.IsTerminated);

        if (contract is null)
        {
            violations.Add(new TradeRuleFinding(
                NoContractCode,
                $"{player.FullName} has no live contract with {fromTeam.Name}, so there is no salary to trade.",
                movement.FromTeamId));
            return;
        }

        movedContracts[movement.AssetKey] = contract;

        if (!player.IsInjured)
        {
            return;
        }

        var injury = player.CurrentInjury?.Description ?? "an injury";
        switch (context.TradeRules.InjuredPlayerEligibility)
        {
            case InjuredPlayerTradeEligibility.Blocked:
                violations.Add(new TradeRuleFinding(
                    InjuredBlockedCode,
                    $"{player.FullName} is injured ({injury}) and this league does not allow injured players to be traded.",
                    movement.FromTeamId));
                break;

            case InjuredPlayerTradeEligibility.AllowedWithWarning:
                warnings.Add(new TradeRuleFinding(
                    InjuredWarningCode,
                    $"{player.FullName} is injured ({injury}). The trade is legal, but the receiving team takes on the injury with the contract.",
                    movement.ToTeamId));
                break;
        }
    }

    private void ValidatePickMovement(
        TradeAssetMovement movement,
        TradeContext context,
        IReadOnlyDictionary<TeamId, Team> teamsById,
        List<TradeRuleFinding> violations)
    {
        var fromTeam = teamsById[movement.FromTeamId];
        var toTeam = teamsById[movement.ToTeamId];
        var pickId = movement.DraftPickId!;

        // Picks belong to franchises, not to a season's squad, so the trade's teams are resolved to
        // the organisations behind them before the pick rules see them.
        var result = _pickRules.ValidateTransfer(
            context.DraftAssets,
            pickId,
            fromTeam.FranchiseId,
            toTeam.FranchiseId,
            context.CurrentSeason,
            context.DraftRules);

        if (result.IsFailure)
        {
            violations.AddRange(result.Errors.Select(error =>
                new TradeRuleFinding(error.Code, error.Message, movement.FromTeamId)));
        }
    }

    /// <summary>
    /// Projects one team's books and roster on the other side of the trade. Nothing is applied: the
    /// charges are rebuilt against the team each contract <em>would</em> belong to, and the same
    /// <see cref="CapLedger"/> the cap sheet uses turns them into threshold standings.
    /// </summary>
    private DomainOperationResult<TradeTeamOutcome> BuildOutcome(
        TradeProposal proposal,
        TradeContext context,
        Team team,
        IReadOnlyDictionary<string, Contract> movedContracts)
    {
        var season = context.CurrentSeason;
        var chargesBefore = CapChargeProjection.ForTeamSeason(context.Contracts, team.Id, season);
        var payrollBefore = Money.Sum(chargesBefore.Select(charge => charge.Amount));

        var sent = proposal.SentBy(team.Id);
        var received = proposal.ReceivedBy(team.Id);

        var outgoingContracts = sent
            .Where(movement => movement.Kind == TradeAssetKind.Player)
            .Select(movement => movedContracts.GetValueOrDefault(movement.AssetKey))
            .OfType<Contract>()
            .ToList();

        var incomingContracts = received
            .Where(movement => movement.Kind == TradeAssetKind.Player)
            .Select(movement => movedContracts.GetValueOrDefault(movement.AssetKey))
            .OfType<Contract>()
            .ToList();

        var outgoingSalary = Money.Sum(outgoingContracts
            .Select(contract => contract.ChargeFor(season)?.Amount ?? Money.Zero));

        var incomingSalary = Money.Sum(incomingContracts
            .Select(contract => contract.ChargeFor(season)?.Amount ?? Money.Zero));

        var outgoingContractIds = outgoingContracts.Select(contract => contract.Id).ToHashSet();

        var chargesAfter = chargesBefore
            .Where(charge => !outgoingContractIds.Contains(charge.ContractId))
            .ToList();

        chargesAfter.AddRange(incomingContracts
            .Select(contract => (Contract: contract, Charge: contract.ChargeFor(season)))
            .Where(pair => pair.Charge is not null)
            .Select(pair => CapCharge.ActiveContract(
                team.Id,
                season,
                pair.Contract.PlayerId,
                pair.Contract.Id,
                pair.Charge!.Amount)));

        var capSheetResult = _capLedger.Evaluate(team.Id, season, chargesAfter, context.CapThresholds);
        if (capSheetResult.IsFailure)
        {
            return DomainOperationResult<TradeTeamOutcome>.Failure(capSheetResult.Errors.ToArray());
        }

        var capSheet = capSheetResult.Value;
        var picksBefore = context.DraftAssets.PicksControlledBy(team.FranchiseId).Count;
        var picksAfter = picksBefore
            - sent.Count(movement => movement.Kind == TradeAssetKind.DraftPick)
            + received.Count(movement => movement.Kind == TradeAssetKind.DraftPick);

        var rosterAfter = team.RosterCount
            - sent.Count(movement => movement.Kind == TradeAssetKind.Player)
            + received.Count(movement => movement.Kind == TradeAssetKind.Player);

        return DomainOperationResult<TradeTeamOutcome>.Success(new TradeTeamOutcome(
            team.Id,
            incomingSalary,
            outgoingSalary,
            payrollBefore,
            capSheet.TotalPayroll,
            team.RosterCount,
            rosterAfter,
            picksBefore,
            picksAfter,
            capSheet.Thresholds));
    }

    private static void CheckRosterLimits(
        TradeTeamOutcome outcome,
        TradeContext context,
        List<TradeRuleFinding> violations)
    {
        if (outcome.RosterCountAfter > context.RosterLimits.MaximumPlayers)
        {
            violations.Add(new TradeRuleFinding(
                RosterMaximumCode,
                $"The trade would leave this team with {outcome.RosterCountAfter} players, above the configured maximum of {context.RosterLimits.MaximumPlayers}.",
                outcome.TeamId));
        }

        if (outcome.RosterCountAfter < context.RosterLimits.MinimumPlayers)
        {
            violations.Add(new TradeRuleFinding(
                RosterMinimumCode,
                $"The trade would leave this team with {outcome.RosterCountAfter} players, below the configured minimum of {context.RosterLimits.MinimumPlayers}.",
                outcome.TeamId));
        }
    }

    /// <summary>
    /// The money rules, in the order a GM meets them: can you take this much back, does it put you
    /// over a line you cannot cross, and does it move you into a band that restricts you.
    /// </summary>
    private static void CheckMoney(
        TradeTeamOutcome outcome,
        TradeContext context,
        List<TradeRuleFinding> violations,
        List<TradeRuleFinding> warnings)
    {
        var rules = context.TradeRules;
        var thresholds = context.CapThresholds;

        var incoming = outcome.IncomingSalary.SmallestUnits;
        var outgoing = outcome.OutgoingSalary.SmallestUnits;

        // Two ways to be allowed to take salary back, and a team gets whichever is kinder: the room
        // it has under the cap once its own outgoing salary is off the books, or a matched share of
        // what it is sending out.
        var payrollAfterSending = outcome.PayrollBefore.SmallestUnits - outgoing;
        var roomAllowance = Math.Max(0, thresholds.SoftCap.SmallestUnits - payrollAfterSending);
        var matchedAllowance = (outgoing * rules.SalaryMatchPercent / 100) + rules.SalaryMatchAllowance.SmallestUnits;
        var allowedIncoming = Math.Max(roomAllowance, matchedAllowance);

        if (incoming > allowedIncoming)
        {
            violations.Add(new TradeRuleFinding(
                SalaryMatchCode,
                $"This team takes back {incoming} against {outgoing} sent out, and may take back at most {allowedIncoming} — {rules.SalaryMatchPercent}% of outgoing salary plus the configured allowance, or the room it has under the soft cap, whichever is larger.",
                outcome.TeamId));
        }

        var payrollAfter = outcome.PayrollAfter.SmallestUnits;

        if (payrollAfter > thresholds.HardCap.SmallestUnits)
        {
            violations.Add(new TradeRuleFinding(
                HardCapCode,
                $"The trade would put this team at {payrollAfter}, above the hard cap of {thresholds.HardCap.SmallestUnits}. No transaction may cross that line.",
                outcome.TeamId));
        }

        if (rules.SecondApronBlocksSalaryIncrease &&
            payrollAfter > thresholds.SecondApron.SmallestUnits &&
            incoming > outgoing)
        {
            violations.Add(new TradeRuleFinding(
                SecondApronCode,
                $"This team finishes above the second apron and would take on more salary than it sends out ({incoming} in against {outgoing} out). Above that line this ruleset allows no net increase.",
                outcome.TeamId));
        }

        WarnOnCrossing(outcome, thresholds.LuxuryTax, CrossesTaxCode, "the luxury tax line", warnings);
        WarnOnCrossing(outcome, thresholds.FirstApron, CrossesApronCode, "the first apron", warnings);
        WarnOnCrossing(outcome, thresholds.SecondApron, CrossesApronCode, "the second apron", warnings);
    }

    /// <summary>
    /// Non-blocking, and worth saying anyway: a legal trade that quietly moves a team into a
    /// restricted band has cost them options they will only discover on their next trade.
    /// </summary>
    private static void WarnOnCrossing(
        TradeTeamOutcome outcome,
        Money threshold,
        string ruleCode,
        string description,
        List<TradeRuleFinding> warnings)
    {
        var before = outcome.PayrollBefore.SmallestUnits;
        var after = outcome.PayrollAfter.SmallestUnits;
        var line = threshold.SmallestUnits;

        if (before <= line && after > line)
        {
            warnings.Add(new TradeRuleFinding(
                ruleCode,
                $"The trade takes this team over {description}, from {before} to {after}.",
                outcome.TeamId));
        }
    }
}
