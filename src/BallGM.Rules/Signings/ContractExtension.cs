using BallGM.Domain.Common;
using BallGM.Domain.Contracts;
using BallGM.Domain.Leagues;
using BallGM.Domain.Negotiations;
using BallGM.Domain.Players;
using BallGM.Domain.Teams;
using BallGM.Domain.Transactions;
using BallGM.Rules.Configuration;
using BallGM.Rules.Negotiations;

namespace BallGM.Rules.Signings;

/// <summary>
/// Re-signing a player before he reaches free agency: his own team offers a new contract, starting
/// the season after the current one ends, while he is in its final season.
/// <para>
/// Legal if the offer comes from his team, he is extension-eligible (final season, not already
/// extended), it starts right where the current contract ends, and its terms pass the same offer
/// legality a signing does — with the incumbent team's longer maximum length. No signing route is
/// checked: an extension spends no cap room today, it commits future seasons, which the payroll
/// outlook then shows.
/// </para>
/// <para>
/// The player takes it if the first season reaches what he will go down to — the same reservation
/// the open market applies (<see cref="PreferenceModel.ReservationPercentOfAsk"/> of the asking
/// price from <see cref="PreferenceModel.AskingPriceFor"/>) — and refuses otherwise, so he never
/// turns down from his own team a figure he would take in free agency.
/// Deterministic on purpose: the same offer gets the same answer, so a refusal is something a GM
/// can act on (offer more, or plan for him leaving) rather than a dice roll to retry.
/// </para>
/// </summary>
public static class ContractExtension
{
    public const string AcceptedCode = "extension.accepted";
    public const string RefusedCode = "extension.refused_below_asking_price";
    public const string NotIncumbentCode = "extension.not_his_team";
    public const string NoContractCode = "extension.no_current_contract";
    public const string NotFinalSeasonCode = "extension.not_in_final_season";
    public const string AlreadyExtendedCode = "extension.already_extended";
    public const string WrongStartCode = "extension.must_start_when_current_ends";
    public const string NotConsideredCode = "extension.not_considered";

    public static ExtensionAssessment Assess(
        Offer offer,
        Team team,
        Player player,
        IReadOnlyCollection<Contract> contracts,
        Season currentSeason,
        NegotiationRules rules,
        CapThresholds thresholds)
    {
        ArgumentNullException.ThrowIfNull(offer);
        ArgumentNullException.ThrowIfNull(team);
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(contracts);

        var violations = new List<RuleFinding>();
        var notes = new List<RuleFinding>();

        if (!team.PlayerIds.Contains(player.Id) || offer.TeamId != team.Id)
        {
            violations.Add(new RuleFinding(NotIncumbentCode, $"{player.FullName} does not play for {team.Name}; only his own team can extend him.", offer.TeamId));
        }

        var live = contracts.Where(contract => contract.PlayerId == player.Id && contract.TeamId == team.Id && !contract.IsTerminated).ToList();
        var current = live.FirstOrDefault(contract => contract.TermFor(currentSeason) is not null);
        if (current is null)
        {
            violations.Add(new RuleFinding(NoContractCode, $"{player.FullName} has no contract with {team.Name} this season to extend.", offer.TeamId));
        }
        else
        {
            if (live.Any(contract => contract.FirstSeason.Year > current.LastSeason.Year))
            {
                violations.Add(new RuleFinding(AlreadyExtendedCode, $"{player.FullName} has already signed an extension.", offer.TeamId));
            }
            else if (current.LastSeason.Year != currentSeason.Year)
            {
                violations.Add(new RuleFinding(NotFinalSeasonCode,
                    $"{player.FullName} is under contract through {current.LastSeason.Year}; he can be extended in the final season of his deal ({current.LastSeason.Year}).", offer.TeamId));
            }

            if (offer.FirstSeason.Year != current.LastSeason.Year + 1)
            {
                violations.Add(new RuleFinding(WrongStartCode,
                    $"An extension starts the season after the current contract ends ({current.LastSeason.Year + 1}), not {offer.FirstSeason.Year}.", offer.TeamId));
            }
        }

        OfferLegality.Check(offer, rules, thresholds, player.SeasonsOfService, isIncumbentTeam: true, violations, notes);

        var ask = PreferenceModel.AskingPriceFor(player, rules, thresholds);
        if (violations.Count > 0)
        {
            return new ExtensionAssessment(false, false, ask, NotConsideredCode,
                "The offer is not one the rules allow, so the player was not asked.", violations, notes);
        }

        var reservation = ask is null ? null : new Money(ask.SmallestUnits * PreferenceModel.ReservationPercentOfAsk / 100);
        var accepted = reservation is null || offer.FirstSeasonCompensation >= reservation;
        return new ExtensionAssessment(
            true,
            accepted,
            ask,
            accepted ? AcceptedCode : RefusedCode,
            accepted
                ? $"{player.FullName} accepts: {Show(offer.FirstSeasonCompensation)} a season meets what he is asking{(ask is null ? string.Empty : $" ({Show(ask)})")}."
                : $"{player.FullName} refuses to re-sign for {Show(offer.FirstSeasonCompensation)} a season. He is asking for {Show(ask!)}, will not go below {Show(reservation!)}, and will test free agency unless the offer improves.",
            violations,
            notes);
    }

    /// <summary>
    /// Records an accepted extension as a new contract and a ledger line. Refuses to execute anything
    /// the assessment did not both allow and see accepted.
    /// </summary>
    public static DomainOperationResult<Contract> Execute(
        Offer offer,
        ExtensionAssessment assessment,
        Player player,
        Season currentSeason,
        TransactionLedger ledger,
        ContractId contractId)
    {
        ArgumentNullException.ThrowIfNull(offer);
        ArgumentNullException.ThrowIfNull(assessment);
        ArgumentNullException.ThrowIfNull(ledger);

        if (!assessment.IsLegal || !assessment.Accepted)
        {
            return DomainOperationResult<Contract>.Failure(new DomainError(assessment.DecisionCode, assessment.DecisionExplanation));
        }

        var contract = Contract.Create(contractId, offer.TeamId, offer.PlayerId, offer.Terms);
        if (contract.IsFailure)
        {
            return contract;
        }

        ledger.Record(
            TransactionKind.ContractExtended,
            currentSeason,
            offer.TeamId,
            $"{player.FullName} signed a {offer.Terms.Count}-season extension from {offer.FirstSeason.Year}.",
            offer.PlayerId,
            contract.Value.Id,
            offer.FirstSeasonCompensation);
        return contract;
    }

    private static string Show(Money money) =>
        string.Create(System.Globalization.CultureInfo.InvariantCulture, $"${money.SmallestUnits / 1_000_000d:0.0}M");
}
