using BallGM.Domain.AI;
using BallGM.Domain.Common;
using BallGM.Domain.Contracts;
using BallGM.Domain.DraftAssets;
using BallGM.Domain.Players;
using BallGM.Domain.Teams;
using BallGM.Domain.Trades;
using BallGM.Rules.Configuration;
using BallGM.Rules.Negotiations;
using BallGM.Rules.Trades;

namespace BallGM.Rules.AI;

/// <summary>One salary dump: the trade, the two sides, and why both made it.</summary>
public sealed record SalaryDump(
    TradeProposal Proposal,
    TeamId FromTeamId,
    TeamId ToTeamId,
    PlayerId PlayerId,
    IReadOnlyList<DraftPickId> Picks,
    RuleFinding Rationale);

/// <summary>
/// Salary dumps (<c>docs/franchise-realism.md</c> → F5): a team over the luxury tax pays a rebuilding
/// team with cap room to take an overpaid contract off its hands, in draft picks. The taxpayer saves
/// the salary and the tax on it; the rebuilding team spends room it was not going to use well on a
/// player it does not need, and is paid in the currency it does need. The price is the overpay — what
/// the contract pays above what the player would earn on the open market, over every season left —
/// in picks valued as a share of the cap: <see cref="FirstRoundPickCapPercent"/> for a first,
/// <see cref="SecondRoundPickCapPercent"/> for a second. A contract that would take more than
/// <see cref="MaximumFirstRoundPicks"/> firsts to move does not move.
/// <para>
/// Pure and deterministic: teams in identifier order, every trade re-validated by
/// <see cref="TradeValidator"/>, so a dump the rules would refuse (salary matching, roster limits, a
/// league's limit on trading future firsts) is never proposed. One dump a team a summer, on either side.
/// </para>
/// </summary>
public static class SalaryDumpModel
{
    private const string DumpCode = "ai_trade.salary_dump";

    /// <summary>The least a contract must overpay a season, in per cent of the cap, to be worth paying to move.</summary>
    public const int MinimumOverpayPercentOfCap = 3;

    /// <summary>What a first-round pick buys, in per cent of the cap of total overpay absorbed.</summary>
    public const int FirstRoundPickCapPercent = 12;

    /// <summary>What a second-round pick buys, in per cent of the cap of total overpay absorbed.</summary>
    public const int SecondRoundPickCapPercent = 2;

    /// <summary>The most firsts a team will attach to a dump.</summary>
    public const int MaximumFirstRoundPicks = 2;

    /// <summary>The most seconds a team will attach to a dump.</summary>
    public const int MaximumSecondRoundPicks = 2;

    /// <summary>
    /// The tax a dump avoids, per cent of the payroll it takes back under the line: a typical bottom
    /// bracket. A rough figure on purpose — it decides only whether a dump is worth its picks.
    /// </summary>
    public const int TaxRatePercent = 150;

    /// <summary>The most a team pays in pick value for a dump, as a multiple of the tax it avoids this season.</summary>
    public const int MaximumPickValueToTaxAvoided = 2;

    /// <summary>A team's best players are never dumped: they are why it pays the tax.</summary>
    public const int ProtectedTopPlayers = 5;

    public static IReadOnlyList<SalaryDump> Find(
        TradeContext context,
        IReadOnlyDictionary<TeamId, CompetitiveDirection> directions,
        NegotiationRules negotiationRules,
        SalaryScale? scale,
        IReadOnlySet<TeamId>? excludedTeams = null,
        bool inSeason = false,
        InjuryReport? injuries = null)
    {
        injuries ??= InjuryReport.None;
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(directions);
        ArgumentNullException.ThrowIfNull(negotiationRules);

        var softCap = context.CapThresholds.SoftCap?.SmallestUnits;
        var line = (context.CapThresholds.LuxuryTax ?? context.CapThresholds.FirstApron)?.SmallestUnits;
        if (softCap is not long cap || line is not long taxLine)
        {
            return [];
        }

        var season = context.CurrentSeason;
        var asOf = new DateOnly(season.Year, 7, 1);
        var playersById = context.Players.ToDictionary(player => player.Id);
        var teamsByFranchise = context.Teams.ToDictionary(team => team.FranchiseId);
        var payroll = context.Teams.ToDictionary(team => team.Id, team => OffseasonModel.PayrollFor(team.Id, context.Contracts, season).SmallestUnits);
        var used = new HashSet<TeamId>(excludedTeams ?? new HashSet<TeamId>());
        // A team under the payroll floor takes on salary whatever its direction: it pays the shortfall
        // otherwise, so being paid in picks to reach the floor is free money.
        var floorLine = context.CapThresholds.PayrollFloor?.SmallestUnits ?? 0;
        var minimumSalary = negotiationRules.CompensationFloor.FloorFor(0)?.SmallestUnits ?? 0;
        long SalaryOf(PlayerId playerId, TeamId teamId) => context.Contracts
            .FirstOrDefault(contract => contract.PlayerId == playerId && contract.TeamId == teamId && !contract.IsTerminated && contract.TermFor(season) is not null)?
            .TermFor(season)!.Compensation.SmallestUnits ?? 0;
        var validator = new TradeValidator();
        var dumps = new List<SalaryDump>();

        foreach (var dumper in context.Teams
            .Where(team => payroll[team.Id] > taxLine && !used.Contains(team.Id))
            .OrderByDescending(team => payroll[team.Id])
            .ThenBy(team => team.Id.Value, StringComparer.Ordinal)
            .ToList())
        {
            var roster = dumper.PlayerIds.Where(playersById.ContainsKey).Select(id => playersById[id]).ToList();
            // A veteran out long is not why his team pays the tax any more: his deal can be moved.
            var protectedIds = roster
                .Where(player => !injuries.IsCareerThreatened(player, asOf))
                .OrderByDescending(player => player.Rating.Overall)
                .ThenBy(player => player.Id.Value, StringComparer.Ordinal)
                .Take(ProtectedTopPlayers)
                .Select(player => player.Id)
                .ToHashSet();

            var candidates = new List<(Player Player, long Salary, long Overpay, int LastYear)>();
            foreach (var player in roster.Where(player => !protectedIds.Contains(player.Id)))
            {
                var contract = context.Contracts.FirstOrDefault(candidate =>
                    candidate.PlayerId == player.Id && candidate.TeamId == dumper.Id && !candidate.IsTerminated && candidate.TermFor(season) is not null);
                if (contract is null || (!inSeason && contract.FirstSeason.Year == season.Year))
                {
                    continue;
                }

                var ask = PreferenceModel.AskingPriceFor(player, negotiationRules, context.CapThresholds, scale)?.SmallestUnits ?? 0;

                // A veteran's long injury is priced in: what he is worth falls the longer he is out.
                if (injuries.IsCareerThreatened(player, asOf))
                {
                    ask = ask * (100 - Math.Min(PreferenceModel.MaximumInjuryDiscountPercent, injuries.DaysOutFor(player.Id) * PreferenceModel.MaximumInjuryDiscountPercent / PreferenceModel.InjuryDiscountFullDays)) / 100;
                }
                var salary = contract.TermFor(season)!.Compensation.SmallestUnits;
                if ((salary - ask) * 100 < cap * MinimumOverpayPercentOfCap)
                {
                    continue;
                }

                var remaining = contract.Terms
                    .Where(term => term.Season.Year >= season.Year && contract.TermFor(term.Season) is not null)
                    .ToList();
                var overpay = remaining.Sum(term => Math.Max(0, term.Compensation.SmallestUnits - ask));
                candidates.Add((player, salary, overpay, remaining.Max(term => term.Season.Year)));
            }

            var dumped = false;
            foreach (var (player, salary, overpay, lastYear) in candidates
                .OrderByDescending(candidate => candidate.Overpay)
                .ThenBy(candidate => candidate.Player.Id.Value, StringComparer.Ordinal))
            {
                var firsts = (int)(overpay / (cap * FirstRoundPickCapPercent / 100));
                var remainder = overpay - (firsts * cap * FirstRoundPickCapPercent / 100);
                var seconds = (int)((remainder + (cap * SecondRoundPickCapPercent / 100) - 1) / (cap * SecondRoundPickCapPercent / 100));
                if (seconds > MaximumSecondRoundPicks)
                {
                    firsts++;
                    seconds = 0;
                }

                if (firsts > MaximumFirstRoundPicks)
                {
                    continue;
                }

                // Worth it only against the tax it saves: a team a hair over the line does not pay two
                // firsts to get under it.
                var taxAvoided = Math.Min(payroll[dumper.Id] - taxLine, salary) * TaxRatePercent / 100;
                var pickValue = ((long)firsts * cap * FirstRoundPickCapPercent / 100) + ((long)seconds * cap * SecondRoundPickCapPercent / 100);
                if (pickValue > taxAvoided * MaximumPickValueToTaxAvoided)
                {
                    continue;
                }

                // The latest picks first: a team pays with the draft furthest from the window it is chasing.
                var owned = context.DraftAssets.PicksControlledBy(dumper.FranchiseId)
                    .Where(pick => pick.DraftSeason.Year > season.Year && pick.DraftSeason.Year <= season.Year + context.DraftRules.TradableFutureDraftHorizon)
                    .OrderByDescending(pick => pick.DraftSeason.Year)
                    .ThenBy(pick => pick.OriginalFranchiseId.Value, StringComparer.Ordinal)
                    .ToList();
                var picks = SpacedPicks(owned.Where(pick => pick.Round == 1), firsts, context.DraftRules.RetainedRoundNumber == 1 ? context.DraftRules.RetainedRoundInterval : 1)
                    .Concat(SpacedPicks(owned.Where(pick => pick.Round == 2), seconds, context.DraftRules.RetainedRoundNumber == 2 ? context.DraftRules.RetainedRoundInterval : 1))
                    .ToList();
                if (picks.Count(pick => pick.Round == 1) < firsts || picks.Count(pick => pick.Round == 2) < seconds)
                {
                    continue;
                }

                foreach (var receiver in context.Teams
                    .Where(team => team.Id != dumper.Id && !used.Contains(team.Id) &&
                        (directions.GetValueOrDefault(team.Id) == CompetitiveDirection.Rebuilding || payroll[team.Id] < floorLine) &&
                        payroll[team.Id] + salary <= cap &&
                        team.RosterCount < team.RosterLimits.MaximumPlayers)
                    .OrderBy(team => payroll[team.Id])
                    .ThenBy(team => team.Id.Value, StringComparer.Ordinal))
                {
                    // A taxpayer at its roster minimum takes a cheap end-of-bench player back, as a real
                    // dump does, so it is not left short; the rebuilding team keeps its roster size.
                    Player? filler = null;
                    if (dumper.RosterCount <= dumper.RosterLimits.MinimumPlayers)
                    {
                        filler = receiver.PlayerIds
                            .Where(playersById.ContainsKey)
                            .Select(id => playersById[id])
                            .Select(candidate => (Player: candidate, Salary: SalaryOf(candidate.Id, receiver.Id)))
                            .Where(candidate => candidate.Salary is > 0 && candidate.Salary <= minimumSalary * 3 / 2)
                            .OrderBy(candidate => candidate.Player.Rating.Overall)
                            .ThenBy(candidate => candidate.Player.Id.Value, StringComparer.Ordinal)
                            .Select(candidate => candidate.Player)
                            .FirstOrDefault();
                        if (filler is null)
                        {
                            continue;
                        }
                    }

                    var movements = new List<TradeAssetMovement> { TradeAssetMovement.Player(player.Id, dumper.Id, receiver.Id) };
                    movements.AddRange(picks.Select(pick => TradeAssetMovement.DraftPick(pick.Id, dumper.Id, receiver.Id)));
                    if (filler is not null)
                    {
                        movements.Add(TradeAssetMovement.Player(filler.Id, receiver.Id, dumper.Id));
                    }

                    var proposal = TradeProposal.Create(
                        new TradeId($"AI-DUMP-{season.Year}-{dumper.Id.Value}-{player.Id.Value}-TO-{receiver.Id.Value}"),
                        season,
                        [dumper.Id, receiver.Id],
                        movements,
                        LeagueStateToken.From(context.Ledger));
                    if (proposal.IsFailure)
                    {
                        continue;
                    }

                    var assessment = validator.Validate(proposal.Value, context);
                    if (assessment.IsFailure || !assessment.Value.IsLegal)
                    {
                        continue;
                    }

                    var price = picks.Count == 0
                        ? "nothing but the room"
                        : string.Join(", ", picks.Select(pick =>
                            $"{(teamsByFranchise.TryGetValue(pick.OriginalFranchiseId, out var original) && original.Id != dumper.Id ? original.Name + "'s " : string.Empty)}{pick.DraftSeason.Year} {(pick.Round == 1 ? "first" : "second")}-round pick"));
                    var injured = injuries.IsCareerThreatened(player, asOf) ? $" — out {injuries.DaysOutFor(player.Id)} more days, his deal now mostly dead weight —" : string.Empty;
                    dumps.Add(new SalaryDump(proposal.Value, dumper.Id, receiver.Id, player.Id, picks.Select(pick => pick.Id).ToList(), new RuleFinding(DumpCode,
                        $"{dumper.Name}, {ExplanationText.Money(payroll[dumper.Id] - taxLine)} over the tax, sends {player.FullName} ({player.Rating.Overall}, {ExplanationText.Money(salary)} a season through {lastYear}){injured} to {receiver.Name}, who {(payroll[receiver.Id] < floorLine ? "are under the payroll floor" : "are rebuilding")} with {ExplanationText.Money(cap - payroll[receiver.Id])} of room — paid {ExplanationText.Money(overpay)} more than the market would pay him — with {price} to take him{(filler is null ? string.Empty : $", and takes {filler.FullName} back on a minimum deal to keep its roster full")}.",
                        dumper.Id)));
                    used.Add(dumper.Id);
                    used.Add(receiver.Id);
                    var back = filler is null ? 0 : SalaryOf(filler.Id, receiver.Id);
                    payroll[dumper.Id] += back - salary;
                    payroll[receiver.Id] += salary - back;
                    dumped = true;
                    break;
                }

                if (dumped)
                {
                    break;
                }
            }
        }

        return dumps;
    }

    /// <summary>
    /// Up to <paramref name="count"/> picks, latest first, at least <paramref name="interval"/> drafts
    /// apart — so a league that makes a franchise keep its pick in a round every so many drafts is
    /// never offered a set it would refuse.
    /// </summary>
    internal static IEnumerable<DraftPick> SpacedPicks(IEnumerable<DraftPick> latestFirst, int count, int interval)
    {
        var chosen = new List<DraftPick>();
        foreach (var pick in latestFirst)
        {
            if (chosen.Count >= count)
            {
                break;
            }

            if (chosen.All(taken => Math.Abs(taken.DraftSeason.Year - pick.DraftSeason.Year) >= Math.Max(1, interval)))
            {
                chosen.Add(pick);
            }
        }

        return chosen;
    }
}
