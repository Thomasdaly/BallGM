using BallGM.Domain.AI;
using BallGM.Domain.Common;
using BallGM.Domain.Contracts;
using BallGM.Domain.Leagues;
using BallGM.Domain.Players;
using BallGM.Domain.Teams;
using BallGM.Rules.Configuration;
using BallGM.Rules.Negotiations;

namespace BallGM.Rules.AI;

/// <summary>A team's decision to buy one player out: the contract, the share of his money kept, and why both sides agree.</summary>
public sealed record BuyoutDecision(TeamId TeamId, PlayerId PlayerId, ContractId ContractId, int RetainedPercent, RuleFinding Rationale);

/// <summary>
/// When an AI team buys a player out (<c>docs/franchise-realism.md</c> → F4). Deliberately narrow,
/// because the competitor's failure was buyouts as a routine way to move good players:
/// <list type="bullet">
/// <item>only a team that is not contending — a contender keeps its veterans;</item>
/// <item>only a veteran (<see cref="MinimumAge"/>+) who is outside the team's rotation, never one of
/// its <see cref="RotationSize"/> best;</item>
/// <item>only one who is clearly overpaid — his asking price on the open market below
/// <see cref="OverpaidPercent"/> per cent of what he is paid this season;</item>
/// <item>at most one a team a summer, the most overpaid first.</item>
/// </list>
/// <para>
/// <b>Why the player agrees.</b> He gives back no more than he expects to earn elsewhere this season
/// (his asking price), and never more than the league's stated cap on the give-back: he ends up whole,
/// free to choose his next team, so consent is not assumed — it is what the terms are built to give.
/// The team keeps the rest of the money as dead money, which is why a contender never bothers.
/// </para>
/// </summary>
public static class BuyoutModel
{
    private const string BuyoutCode = "ai_buyout.agreed";

    /// <summary>The youngest player a team will buy out: a buyout is how a veteran's contract ends early.</summary>
    public const int MinimumAge = 30;

    /// <summary>How many of a team's best players are never bought out.</summary>
    public const int RotationSize = 8;

    /// <summary>A player is overpaid when his asking price is below this share of his salary, in per cent.</summary>
    public const int OverpaidPercent = 60;

    public static BuyoutDecision? Decide(
        Team team,
        IReadOnlyList<Player> roster,
        IReadOnlyCollection<Contract> contracts,
        Season currentSeason,
        NegotiationRules rules,
        CapThresholds thresholds,
        BuyoutRules buyouts,
        CompetitiveDirection direction,
        DateOnly asOf,
        SalaryScale? scale = null)
    {
        ArgumentNullException.ThrowIfNull(team);
        ArgumentNullException.ThrowIfNull(roster);
        ArgumentNullException.ThrowIfNull(contracts);
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(thresholds);
        ArgumentNullException.ThrowIfNull(buyouts);

        if (!buyouts.IsConfigured || direction == CompetitiveDirection.Contending || team.RosterCount <= team.RosterLimits.MinimumPlayers)
        {
            return null;
        }

        var rotation = roster
            .OrderByDescending(player => player.Rating.Overall)
            .ThenBy(player => player.Id.Value, StringComparer.Ordinal)
            .Take(RotationSize)
            .Select(player => player.Id)
            .ToHashSet();

        BuyoutDecision? best = null;
        var bestOverpay = 0L;
        foreach (var player in roster.OrderBy(player => player.Id.Value, StringComparer.Ordinal))
        {
            var age = player.AgeOn(asOf);
            if (age < MinimumAge || rotation.Contains(player.Id))
            {
                continue;
            }

            var contract = contracts.FirstOrDefault(candidate =>
                candidate.PlayerId == player.Id && candidate.TeamId == team.Id && !candidate.IsTerminated && candidate.TermFor(currentSeason) is not null);
            var salary = contract?.TermFor(currentSeason)?.Compensation.SmallestUnits ?? 0;
            var ask = PreferenceModel.AskingPriceFor(player, rules, thresholds, scale)?.SmallestUnits ?? 0;
            if (contract is null || salary == 0 || ask * 100 >= salary * OverpaidPercent)
            {
                continue;
            }

            var remaining = contract.Terms.Where(term => term.Season.Year >= currentSeason.Year).Sum(term => term.GuaranteedAmount.SmallestUnits);
            if (remaining == 0)
            {
                continue;
            }

            var giveBack = Math.Min(ask, remaining * buyouts.MaximumGiveBackPercent / 100);
            var retained = (int)((remaining - giveBack) * 100 / remaining);
            var overpay = salary - ask;
            if (overpay <= bestOverpay)
            {
                continue;
            }

            bestOverpay = overpay;
            best = new BuyoutDecision(team.Id, player.Id, contract.Id, retained, new RuleFinding(BuyoutCode,
                $"{team.Name} ({direction}) buys out {player.FullName} (age {age}, {player.Rating.Overall}, outside its rotation), paid {ExplanationText.Money(salary)} this season against an asking price of {ExplanationText.Money(ask)}: he gives back {ExplanationText.Money(giveBack)} of the {ExplanationText.Money(remaining)} still owed, about what he will earn elsewhere, and {team.Name} carries the rest as dead money.",
                team.Id));
        }

        return best;
    }
}
