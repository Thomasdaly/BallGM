using BallGM.Application.Seasons;
using BallGM.Domain.Common;
using BallGM.Domain.Players;

namespace BallGM.Application.Leagues;

/// <summary>
/// The offseason an unattended league runs at every season boundary (<c>docs/franchise-realism.md</c>
/// → F3): teams keep the players they want and can afford before their contracts run out, then the
/// rest go to a free-agency market where teams bid and each player chooses. Every decision is the AI
/// front office's (<see cref="AI.IFrontOfficeAdvisor"/>) and every signing goes through the same
/// engines a human's does, so nothing here is a second rulebook.
/// </summary>
public sealed partial class LeagueSession
{
    /// <summary>The most rounds a market runs. Prices fall each round, so a market rarely needs them all.</summary>
    public const int MarketRounds = 8;

    /// <summary>
    /// Every team's re-signings, decided in the final season of the expiring contracts (so before the
    /// season is concluded) against the season's own table. Each is offered as an extension at the
    /// player's asking price, which he accepts from his own team; a team's payroll budget, by its
    /// direction, decides how many it can keep.
    /// </summary>
    private (int ReSigned, IReadOnlyList<RuleFinding> Notes) RunReSigningWindow()
    {
        if (_snapshot is null || _seasonRun is null)
        {
            return (0, []);
        }

        var records = _seasonEngine.Standings(_seasonRun, _snapshot).Rows.ToDictionary(row => row.TeamId, row => row.Overall);
        var plans = _frontOfficeAdvisor.PlanReSignings(_snapshot, records);
        if (plans.IsFailure)
        {
            return (0, plans.Errors.Select(error => new RuleFinding(error.Code, error.Message)).ToList());
        }

        var reSigned = 0;
        var notes = new List<RuleFinding>();
        foreach (var plan in plans.Value)
        {
            var outcome = OfferExtension(plan.PlayerId.Value, plan.Seasons, plan.FirstSeasonSalary, annualRaisePercent: 0);
            if (outcome.IsSuccess && outcome.Value.Accepted)
            {
                reSigned++;
                notes.Add(plan.Rationale);
            }
        }

        return (reSigned, notes);
    }

    /// <summary>
    /// The free-agency market: round by round, every team bids for the free agents who would improve
    /// it, inside its budget, and each player takes the offer he prefers; prices fall each round until
    /// the market clears or <see cref="MarketRounds"/> have run. Every accepted offer is executed
    /// through the signing engine, which re-validates it against the league as it then stands.
    /// </summary>
    private (int Signed, IReadOnlyList<RuleFinding> Notes) RunFreeAgencyMarket()
    {
        if (_snapshot is null)
        {
            return (0, []);
        }

        var signed = 0;
        var notes = new List<RuleFinding>();
        for (var round = 0; round < MarketRounds; round++)
        {
            var pool = FreeAgentPool(_snapshot);
            if (pool.Count == 0)
            {
                break;
            }

            var plans = _frontOfficeAdvisor.PlanMarketRound(_snapshot, pool, round);
            if (plans.IsFailure)
            {
                notes.AddRange(plans.Errors.Select(error => new RuleFinding(error.Code, error.Message)));
                break;
            }

            foreach (var plan in plans.Value)
            {
                var execution = _signingEngine.Execute(plan.Offer, _snapshot, plan.Offer.TeamId, plan.Offer.PlayerId);
                if (execution.IsFailure)
                {
                    continue;
                }

                _snapshot = _snapshot with { Contracts = [.. _snapshot.Contracts, execution.Value.Contract] };
                signed++;
                notes.Add(plan.Choice);
            }
        }

        return (signed, notes);
    }

    /// <summary>
    /// The summer's buyouts, before the market opens so a bought-out veteran can sign elsewhere: each
    /// agreed by the front office's own rule and carried out by the signing engine, which refuses one
    /// the league's buyout rules do not allow.
    /// </summary>
    private (IReadOnlyList<BuyoutLine> BoughtOut, IReadOnlyList<RuleFinding> Notes) RunBuyouts()
    {
        if (_snapshot is null)
        {
            return ([], []);
        }

        var plans = _frontOfficeAdvisor.PlanBuyouts(_snapshot);
        if (plans.IsFailure)
        {
            return ([], plans.Errors.Select(error => new RuleFinding(error.Code, error.Message)).ToList());
        }

        var boughtOut = new List<BuyoutLine>();
        var notes = new List<RuleFinding>();
        foreach (var plan in plans.Value)
        {
            if (_signingEngine.ExecuteBuyout(_snapshot, plan.TeamId, plan.PlayerId, plan.ContractId, plan.RetainedPercent).IsSuccess)
            {
                boughtOut.Add(new BuyoutLine(plan.PlayerId.Value, plan.TeamId.Value, plan.RetainedPercent, plan.Rationale.Explanation));
                notes.Add(plan.Rationale);
            }
        }

        return (boughtOut, notes);
    }

    /// <summary>Every unsigned, unretired player, worked out once rather than player by player against every contract.</summary>
    private static List<Player> FreeAgentPool(LeagueSnapshot snapshot)
    {
        var rostered = snapshot.Teams.SelectMany(team => team.PlayerIds).ToHashSet();
        var contracted = snapshot.Contracts
            .Where(contract => !contract.IsTerminated && contract.TermFor(snapshot.CurrentSeason) is not null)
            .Select(contract => contract.PlayerId)
            .ToHashSet();

        return snapshot.Players
            .Where(player => !player.IsRetired && !rostered.Contains(player.Id) && !contracted.Contains(player.Id))
            .OrderBy(player => player.Id.Value, StringComparer.Ordinal)
            .ToList();
    }
}
