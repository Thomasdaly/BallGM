using BallGM.Domain.Cap;
using BallGM.Domain.Common;
using BallGM.Domain.Contracts;
using BallGM.Domain.DraftAssets;
using BallGM.Domain.Franchises;
using BallGM.Domain.Players;
using BallGM.Domain.Teams;
using BallGM.Domain.Trades;
using BallGM.Rules.Cap;
using BallGM.Rules.Configuration;
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
    private const string SalaryMatchingSkippedNoSoftCapCode = "trade.salary_matching_skipped_no_soft_cap";
    private const string SalaryMatchingSkippedNotConfiguredCode = "trade.salary_matching_skipped_not_configured";
    private const string HardCapCheckSkippedCode = "trade.hard_cap_check_skipped_no_hard_cap";
    private const string ApronRestrictionSkippedCode = "trade.apron_restriction_skipped_no_apron";

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
        var violations = new List<RuleFinding>();
        var warnings = new List<RuleFinding>();

        // Which money rules this league does not have, said once for the whole proposal rather than
        // once per team. A check that silently passes because a threshold was absent is
        // indistinguishable from a check that ran and approved, and that is the confusion this task
        // exists to end.
        var notes = SkippedMoneyRules(context);

        // A proposal built against a league that has since moved cannot be trusted: the player in it
        // may already have been traded, the pick may already have conveyed.
        if (proposal.IsStaleAgainst(context.Ledger))
        {
            violations.Add(new RuleFinding(
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
            new TradeAssessment(proposal.Id, violations, warnings, notes, outcomes));
    }

    private void ValidatePlayerMovement(
        TradeAssetMovement movement,
        TradeContext context,
        IReadOnlyDictionary<TeamId, Team> teamsById,
        IReadOnlyDictionary<PlayerId, Player> playersById,
        IDictionary<string, Contract> movedContracts,
        List<RuleFinding> violations,
        List<RuleFinding> warnings)
    {
        var playerId = movement.PlayerId!;
        var fromTeam = teamsById[movement.FromTeamId];

        if (!playersById.TryGetValue(playerId, out var player))
        {
            violations.Add(new RuleFinding(
                UnknownPlayerCode,
                $"Player '{playerId.Value}' is not a player in this league.",
                movement.FromTeamId));
            return;
        }

        if (!fromTeam.PlayerIds.Contains(playerId))
        {
            violations.Add(new RuleFinding(
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
            violations.Add(new RuleFinding(
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
                violations.Add(new RuleFinding(
                    InjuredBlockedCode,
                    $"{player.FullName} is injured ({injury}) and this league does not allow injured players to be traded.",
                    movement.FromTeamId));
                break;

            case InjuredPlayerTradeEligibility.AllowedWithWarning:
                warnings.Add(new RuleFinding(
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
        List<RuleFinding> violations)
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
                new RuleFinding(error.Code, error.Message, movement.FromTeamId)));
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

        // A charge with no contract behind it — a roster-slot hold — is not something the trade can
        // send out, so it stays on the books either way.
        var chargesAfter = chargesBefore
            .Where(charge => charge.ContractId is null || !outgoingContractIds.Contains(charge.ContractId))
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
        List<RuleFinding> violations)
    {
        if (outcome.RosterCountAfter > context.RosterLimits.MaximumPlayers)
        {
            violations.Add(new RuleFinding(
                RosterMaximumCode,
                $"The trade would leave this team with {outcome.RosterCountAfter} players, above the configured maximum of {context.RosterLimits.MaximumPlayers}.",
                outcome.TeamId));
        }

        // A trade may not take a team below the roster minimum, but a team already short of it is not
        // barred from trading — only from getting shorter. Refusing every deal a short squad tries to
        // make would punish it for a state free agency exists to let it fix, and the reason given
        // would be about the roster it turned up with rather than about this trade.
        if (outcome.RosterCountAfter < context.RosterLimits.MinimumPlayers &&
            outcome.RosterCountAfter < outcome.RosterCountBefore)
        {
            violations.Add(new RuleFinding(
                RosterMinimumCode,
                $"The trade would leave this team with {outcome.RosterCountAfter} players, below the configured minimum of {context.RosterLimits.MinimumPlayers}.",
                outcome.TeamId));
        }
    }

    /// <summary>
    /// Names every money rule this league does not configure, as a rule code plus a sentence. These
    /// are notes rather than warnings: a league with no salary matching is not a league with a
    /// problem, and showing "no salary matching applies" in the same colour as "you are trading an
    /// injured player" teaches a GM to read neither.
    /// </summary>
    private static List<RuleFinding> SkippedMoneyRules(TradeContext context)
    {
        var notes = new List<RuleFinding>();
        var thresholds = context.CapThresholds;

        if (thresholds.SoftCap is null)
        {
            notes.Add(new RuleFinding(
                SalaryMatchingSkippedNoSoftCapCode,
                "This league configures no soft cap, so salary matching does not apply: a team may take back any amount of salary."));
        }
        else if (!context.TradeRules.HasSalaryMatching)
        {
            notes.Add(new RuleFinding(
                SalaryMatchingSkippedNotConfiguredCode,
                "This league configures no salary-match percentage, so salary matching does not apply: a team may take back any amount of salary."));
        }

        if (thresholds.HardCap is null)
        {
            notes.Add(new RuleFinding(
                HardCapCheckSkippedCode,
                "This league configures no hard cap, so no trade is refused for the payroll it leaves behind."));
        }

        if (context.TradeRules.SecondApronBlocksSalaryIncrease && thresholds.SecondApron is null)
        {
            notes.Add(new RuleFinding(
                ApronRestrictionSkippedCode,
                "This ruleset restricts salary increases above the second apron, but configures no second apron, so the restriction does not apply."));
        }

        return notes;
    }

    /// <summary>
    /// The money rules, in the order a GM meets them: can you take this much back, does it put you
    /// over a line you cannot cross, and does it move you into a band that restricts you.
    /// <para>
    /// Each of the three skips when the league does not configure the threshold it is anchored to,
    /// as an early return rather than as an accidental consequence of a null. What was skipped
    /// travels with the assessment — see <see cref="SkippedMoneyRules"/>.
    /// </para>
    /// </summary>
    private static void CheckMoney(
        TradeTeamOutcome outcome,
        TradeContext context,
        List<RuleFinding> violations,
        List<RuleFinding> warnings)
    {
        var thresholds = context.CapThresholds;

        CheckSalaryMatching(outcome, context, violations);
        CheckHardCap(outcome, thresholds, violations);
        CheckApronRestriction(outcome, context, violations);

        // A crossing warning for a line the league does not have would be a warning about nothing.
        WarnOnCrossing(outcome, thresholds.LuxuryTax, CrossesTaxCode, "the luxury tax line", warnings);
        WarnOnCrossing(outcome, thresholds.FirstApron, CrossesApronCode, "the first apron", warnings);
        WarnOnCrossing(outcome, thresholds.SecondApron, CrossesApronCode, "the second apron", warnings);
    }

    private static void CheckSalaryMatching(
        TradeTeamOutcome outcome,
        TradeContext context,
        List<RuleFinding> violations)
    {
        var rules = context.TradeRules;
        var softCap = context.CapThresholds.SoftCap;

        // Skipping salary matching: the rule is anchored to the soft cap, since half a team's
        // allowance is the room it has under that line. With no soft cap there is nothing to be over
        // and nothing to match against, so the rule does not exist in this league.
        if (softCap is null)
        {
            return;
        }

        // Skipping salary matching: this league has a soft cap but states no matching percentage.
        if (rules.SalaryMatchPercent is not { } matchPercent)
        {
            return;
        }

        var incoming = outcome.IncomingSalary.SmallestUnits;
        var outgoing = outcome.OutgoingSalary.SmallestUnits;

        // Two ways to be allowed to take salary back, and a team gets whichever is kinder: the room
        // it has under the cap once its own outgoing salary is off the books, or a matched share of
        // what it is sending out.
        var payrollAfterSending = outcome.PayrollBefore.SmallestUnits - outgoing;
        var roomAllowance = Math.Max(0, softCap.SmallestUnits - payrollAfterSending);
        var matchedAllowance = (outgoing * matchPercent / 100) + rules.SalaryMatchAllowance.SmallestUnits;
        var allowedIncoming = Math.Max(roomAllowance, matchedAllowance);

        if (incoming > allowedIncoming)
        {
            violations.Add(new RuleFinding(
                SalaryMatchCode,
                $"This team takes back {incoming} against {outgoing} sent out, and may take back at most {allowedIncoming} — {matchPercent}% of outgoing salary plus the configured allowance, or the room it has under the soft cap, whichever is larger.",
                outcome.TeamId));
        }
    }

    private static void CheckHardCap(
        TradeTeamOutcome outcome,
        CapThresholds thresholds,
        List<RuleFinding> violations)
    {
        // Skipping the ceiling check: this league configures no hard cap, so no payroll is illegal
        // on its own.
        if (thresholds.HardCap is not { } hardCap)
        {
            return;
        }

        var payrollAfter = outcome.PayrollAfter.SmallestUnits;

        if (payrollAfter > hardCap.SmallestUnits)
        {
            violations.Add(new RuleFinding(
                HardCapCode,
                $"The trade would put this team at {payrollAfter}, above the hard cap of {hardCap.SmallestUnits}. No transaction may cross that line.",
                outcome.TeamId));
        }
    }

    private static void CheckApronRestriction(
        TradeTeamOutcome outcome,
        TradeContext context,
        List<RuleFinding> violations)
    {
        if (!context.TradeRules.SecondApronBlocksSalaryIncrease)
        {
            return;
        }

        // Skipping the apron restriction: the ruleset asks for it, but configures no second apron
        // for it to sit above, so there is no band to restrict.
        if (context.CapThresholds.SecondApron is not { } secondApron)
        {
            return;
        }

        var incoming = outcome.IncomingSalary.SmallestUnits;
        var outgoing = outcome.OutgoingSalary.SmallestUnits;

        if (outcome.PayrollAfter.SmallestUnits > secondApron.SmallestUnits && incoming > outgoing)
        {
            violations.Add(new RuleFinding(
                SecondApronCode,
                $"This team finishes above the second apron and would take on more salary than it sends out ({incoming} in against {outgoing} out). Above that line this ruleset allows no net increase.",
                outcome.TeamId));
        }
    }

    /// <summary>
    /// Non-blocking, and worth saying anyway: a legal trade that quietly moves a team into a
    /// restricted band has cost them options they will only discover on their next trade.
    /// </summary>
    private static void WarnOnCrossing(
        TradeTeamOutcome outcome,
        Money? threshold,
        string ruleCode,
        string description,
        List<RuleFinding> warnings)
    {
        if (threshold is null)
        {
            return;
        }

        var before = outcome.PayrollBefore.SmallestUnits;
        var after = outcome.PayrollAfter.SmallestUnits;
        var line = threshold.SmallestUnits;

        if (before <= line && after > line)
        {
            warnings.Add(new RuleFinding(
                ruleCode,
                $"The trade takes this team over {description}, from {before} to {after}.",
                outcome.TeamId));
        }
    }
}
