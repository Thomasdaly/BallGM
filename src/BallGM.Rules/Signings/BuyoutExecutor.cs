using BallGM.Domain.Common;
using BallGM.Domain.Contracts;
using BallGM.Domain.Leagues;
using BallGM.Domain.Players;
using BallGM.Domain.Teams;
using BallGM.Domain.Transactions;
using BallGM.Rules.Configuration;

namespace BallGM.Rules.Signings;

/// <summary>
/// Carries out an agreed buyout: the contract ends from this season with only the retained share of
/// its guaranteed money left as dead money, the player leaves the roster (the roster minimum still
/// holds — a buyout is a release the team chose), and the ledger records it. Validated first and
/// applied all or nothing.
/// </summary>
public static class BuyoutExecutor
{
    private const string NotConfiguredCode = "buyout.not_a_rule_here";
    private const string NotOnTeamCode = "buyout.player_not_on_team";
    private const string NoContractCode = "buyout.no_live_contract";
    private const string GiveBackTooLargeCode = "buyout.give_back_above_limit";

    public static DomainOperationResult Execute(
        Team team,
        Player player,
        Contract contract,
        int retainedPercent,
        Season currentSeason,
        BuyoutRules rules,
        TransactionLedger ledger)
    {
        ArgumentNullException.ThrowIfNull(team);
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(contract);
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(ledger);

        if (!rules.IsConfigured)
        {
            return DomainOperationResult.Failure(new DomainError(NotConfiguredCode, "This league has no buyouts: a player can only be released in full."));
        }

        if (!team.PlayerIds.Contains(player.Id))
        {
            return DomainOperationResult.Failure(new DomainError(NotOnTeamCode, $"{player.FullName} is not on {team.Name}."));
        }

        if (contract.PlayerId != player.Id || contract.TeamId != team.Id || contract.IsTerminated || contract.TermFor(currentSeason) is null)
        {
            return DomainOperationResult.Failure(new DomainError(NoContractCode, $"{player.FullName} has no live contract with {team.Name} this season to buy out."));
        }

        if (100 - retainedPercent > rules.MaximumGiveBackPercent)
        {
            return DomainOperationResult.Failure(new DomainError(GiveBackTooLargeCode,
                $"A buyout here may give back at most {rules.MaximumGiveBackPercent}% of what is owed, not {100 - retainedPercent}%."));
        }

        var removed = team.RemovePlayer(player.Id);
        if (removed.IsFailure)
        {
            return removed;
        }

        var boughtOut = contract.BuyOut(currentSeason, retainedPercent);
        if (boughtOut.IsFailure)
        {
            team.AddPlayer(player.Id);
            return boughtOut;
        }

        ledger.Record(
            TransactionKind.PlayerBoughtOut,
            currentSeason,
            team.Id,
            $"{team.Name} bought out {player.FullName}, who kept {retainedPercent}% of the guaranteed money still owed.",
            player.Id,
            contract.Id,
            contract.TermFor(currentSeason)!.GuaranteedAmount);
        return DomainOperationResult.Success;
    }
}
