using BallGM.Domain.Common;
using BallGM.Domain.Contracts;
using BallGM.Domain.Leagues;
using BallGM.Domain.Negotiations;
using BallGM.Domain.Players;
using BallGM.Domain.Teams;
using BallGM.Domain.Transactions;
using BallGM.Rules.Configuration;

namespace BallGM.Rules.Signings;

/// <summary>
/// Signs a drafted player to his first contract on the league's rookie scale (<see cref="RookieScaleRules"/>):
/// the selection sets the terms, so no signing route is consulted — the generic form of the real
/// agreement's rookie exception, which lets a team over the cap sign its own picks. The roster maximum
/// still holds, and a player already under contract cannot sign at all.
/// </summary>
public static class RookieScaleExecutor
{
    private const string NoScaleCode = "rookie_scale.not_a_rule_here";
    private const string RosterFullCode = "rookie_scale.roster_full";
    private const string UnderContractCode = "rookie_scale.already_under_contract";

    public static DomainOperationResult<Contract> Execute(
        Team team,
        Player player,
        int round,
        int selection,
        Season season,
        RookieScaleRules rules,
        Money? softCap,
        Money minimum,
        IReadOnlyCollection<Contract> contracts,
        TransactionLedger ledger)
    {
        ArgumentNullException.ThrowIfNull(team);
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(contracts);
        ArgumentNullException.ThrowIfNull(ledger);

        var terms = rules.TermsFor(round, selection, season, softCap, minimum);
        if (terms is null)
        {
            return DomainOperationResult<Contract>.Failure(new DomainError(NoScaleCode, "This league has no rookie scale for this pick."));
        }

        if (contracts.Any(contract => contract.PlayerId == player.Id && !contract.IsTerminated && contract.TermFor(season) is not null))
        {
            return DomainOperationResult<Contract>.Failure(new DomainError(UnderContractCode, $"{player.FullName} is already under contract."));
        }

        if (team.RosterCount >= team.RosterLimits.MaximumPlayers)
        {
            return DomainOperationResult<Contract>.Failure(new DomainError(RosterFullCode, $"{team.Name} has no roster spot for {player.FullName}."));
        }

        var contract = Contract.Create(new ContractId(SortableId.NewId()), team.Id, player.Id, terms);
        if (contract.IsFailure)
        {
            return contract;
        }

        var added = team.AddPlayer(player.Id);
        if (added.IsFailure)
        {
            return DomainOperationResult<Contract>.Failure(added.Errors.ToArray());
        }

        ledger.RecordSigning(
            season,
            team.Id,
            player.Id,
            contract.Value.Id,
            terms[0].Compensation,
            SigningRouteKind.RookieScale,
            $"{player.FullName} signed his rookie-scale contract with {team.Name}: {ExplanationText.Count(terms.Count, "season")} from round {round}, pick {selection}, at {ExplanationText.Money(terms[0].Compensation.SmallestUnits)}{(terms.Any(term => term.Option == ContractOptionKind.Team) ? $", the last {ExplanationText.Count(terms.Count(term => term.Option == ContractOptionKind.Team), "season")} the team's option" : string.Empty)}.");
        return contract;
    }
}
