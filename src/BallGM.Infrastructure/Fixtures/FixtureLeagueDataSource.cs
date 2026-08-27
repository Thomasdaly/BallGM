using BallGM.Application.Leagues;
using BallGM.Domain.Common;
using BallGM.Domain.Contracts;
using BallGM.Domain.DraftAssets;
using BallGM.Domain.Franchises;
using BallGM.Domain.Leagues;
using BallGM.Domain.Players;
using BallGM.Domain.Teams;
using BallGM.Domain.Transactions;
using BallGM.Infrastructure.Rulesets;
using BallGM.Infrastructure.Time;
using BallGM.Rules.Configuration;

namespace BallGM.Infrastructure.Fixtures;

/// <summary>
/// Builds the Milestone 2 fictional league: a ruleset genuinely read from disk through
/// <see cref="LeagueRulesetSerializer"/>, plus franchises, teams, and players constructed through
/// the aggregates' own <c>Create(...)</c> factories. Roster sizes come from the loaded file rather
/// than from a constant, so editing <c>data/rulesets/default-league.json</c> visibly changes the
/// league the client renders — that is the moddable-rules path this milestone is proving.
/// <para>
/// Content is generated deterministically from static tables rather than randomly, so the same
/// fixture appears on every launch. Identifiers are still minted fresh per load and are not stable
/// across runs; persisting this league is Milestone 3+ work.
/// </para>
/// </summary>
public sealed class FixtureLeagueDataSource : ILeagueDataSource
{
    private const string MissingRulesetFileCode = "fixture.ruleset_file_missing";
    private const string UnreadableRulesetFileCode = "fixture.ruleset_file_unreadable";

    /// <summary>
    /// The enduring organisations, deliberately named differently from the teams they field, so the
    /// Franchise/Team distinction in <c>docs/domain-language.md</c> is visible on screen instead of
    /// looking like the same string printed twice.
    /// </summary>
    private static readonly string[] FranchiseNames =
    [
        "Harbourline Sporting Club",
        "Cascade Falls Athletic Company",
        "Verdanmoor Basketball Club",
        "Saltpan City Sporting Trust",
        "Northreach Athletic Union",
        "Old Foundry Basketball Company",
    ];

    private static readonly string[] TeamCities =
    [
        "Harbourline",
        "Cascade Falls",
        "Verdanmoor",
        "Saltpan City",
        "Northreach",
        "Old Foundry",
    ];

    private static readonly string[] TeamNicknames =
    [
        "Tidewatch",
        "Ironworks",
        "Kestrels",
        "Prospectors",
        "Aurora",
        "Bellringers",
    ];

    /// <summary>Team quality offsets, so the league reads as contenders and rebuilders rather than clones.</summary>
    private static readonly int[] TeamStrengthOffsets = [6, 3, 0, -2, -5, -8];

    private static readonly string[] GivenNames =
    [
        "Amari", "Dessalin", "Kofi", "Roan", "Tobias", "Emeka", "Ilias", "Zeke",
        "Mattias", "Junior", "Osei", "Ravi", "Nikola", "Cassian", "Odell", "Bram",
        "Teodor", "Salim", "Anders", "Kwabena", "Vidar", "Marcelo", "Idris", "Leandro",
        "Yusuf", "Padraig", "Rune", "Bakary", "Elian", "Tomasz",
    ];

    private static readonly string[] Surnames =
    [
        "Okonkwo", "Vasquez", "Lindqvist", "Aduba", "Marchetti", "Ferreira", "Kovac", "Ahern",
        "Delacroix", "Nwachukwu", "Sorensen", "Bittencourt", "Halloran", "Yilmaz", "Adeyemi",
        "Petrossian", "Cardenas", "Mbeki", "Rasmussen", "Fontaine", "Bergstrom", "Ozturk",
        "Salazar", "Kirwan", "Danilov", "Achebe", "Moreau", "Strand", "Quintero", "Bayode",
    ];

    /// <summary>
    /// One roster's worth of positions: three of each across fifteen slots, but not in rotation
    /// order, so a roster does not read as a repeating PG-SG-SF-PF-C ladder. Rotated per team and
    /// wrapped for roster sizes the ruleset file sets larger or smaller than this plan.
    /// </summary>
    private static readonly Position[] RosterPositionPlan =
    [
        Position.PointGuard,
        Position.SmallForward,
        Position.Center,
        Position.ShootingGuard,
        Position.PowerForward,
        Position.PointGuard,
        Position.Center,
        Position.ShootingGuard,
        Position.SmallForward,
        Position.PowerForward,
        Position.ShootingGuard,
        Position.PointGuard,
        Position.PowerForward,
        Position.Center,
        Position.SmallForward,
    ];

    /// <summary>
    /// Overall ratings down a roster, with uneven gaps: a star, a second option, a starting core,
    /// then a bench that thins out. A straight arithmetic slide made every team look generated.
    /// </summary>
    private static readonly int[] SlotOverallCurve =
    [
        88, 84, 82, 79, 77, 74, 72, 71, 69, 67, 64, 62, 60, 57, 54,
    ];

    /// <summary>Roster slots that carry an injury, so the client shows the unhealthy path too.</summary>
    private static readonly Dictionary<(int TeamIndex, int Slot), string> ScriptedInjuries = new()
    {
        [(0, 4)] = "Sprained left ankle — day to day",
        [(1, 1)] = "Fractured shooting hand — out 6 weeks",
        [(3, 7)] = "Lower back strain — out indefinitely",
        [(4, 0)] = "Right knee soreness — day to day",
    };

    /// <summary>
    /// How many roster spots each team leaves open, counted down from the ruleset's maximum. Not all
    /// full, deliberately: a league with every roster at its limit is a league where free agency
    /// cannot be played at all, and the two teams that fall below the roster <em>minimum</em> are
    /// what puts a roster-slot hold on a cap sheet where a human can see it.
    /// <para>
    /// Only the last team falls below the minimum, and that is deliberate rather than shy: the team
    /// sitting exactly on the soft cap has to stay exactly on it, because "at the line" is its own
    /// case and a hold would push it over. So the league carries one team with holds, one team with
    /// nothing to spare, and four with spare roster spots but no obligation to fill them.
    /// </para>
    /// </summary>
    private static readonly int[] TeamOpenRosterSpots = [0, 1, 2, 3, 3, 5];

    /// <summary>
    /// One unsigned player, as the fixture describes them before the aggregates are built.
    /// </summary>
    private sealed record FreeAgentPlan(
        string FullName,
        Position Position,
        int Overall,
        int Age,
        int SeasonsOfService,
        string? Injury);

    /// <summary>
    /// The unsigned players, and what each of them is for. Every one exists to make a different
    /// decision interesting, because a market of interchangeable players is a list, not a market.
    /// <para>
    /// What each of them <em>wants</em> — a contender, term over average salary, a role — is the
    /// preference model's business and arrives with it. What is here now is the raw material every
    /// one of those preferences reads: age, service, position, and playing strength. The names in
    /// the comments are promises the next milestone has to keep, not behaviour this one implements.
    /// </para>
    /// </summary>
    private static readonly FreeAgentPlan[] FreeAgents =
    [
        // The maximum-money star. Enough service to reach the top compensation tier and good enough
        // that the ceiling, rather than anyone's willingness to pay, is what limits the offer.
        new("Idris Vantongeren", Position.SmallForward, 91, 29, 11, null),

        // The one who will take less to win. Priced well below what he is worth so that a team with
        // only its allowance has something to say that a team with room cannot outbid.
        new("Ravi Okonkwo-Sable", Position.PowerForward, 84, 31, 10, null),

        // The one who wants years rather than the highest average. The term limit is the rule his
        // decision runs into, which is why he has the service to be offered the longest deal.
        new("Matthias Duquesne", Position.Center, 80, 27, 8, null),

        // The one who wants to play. Good enough to start on a thin roster and not on a deep one, so
        // the same offer means different things depending on who makes it.
        new("Teodoro Ashgrove", Position.PointGuard, 76, 24, 4, null),

        // A rookie with no service at all: the compensation floor's bottom band, and the only player
        // here a team above the apron can actually sign.
        new("Nnamdi Ferreiro", Position.ShootingGuard, 68, 21, 0, null),

        // An injured veteran, so the market has a player whose asking price and whose availability
        // point in different directions.
        new("Curtis Vantaa", Position.Center, 73, 33, 12, "Achilles tendinitis — out 4 weeks"),
    ];

    /// <summary>The season the fixture league is sitting in. Fictional, like everything else here.</summary>
    private static readonly Season FixtureSeason = new(2031);

    /// <summary>
    /// Where each team's payroll is aimed, against the shipped ruleset's thresholds (soft cap
    /// 141M, tax 172M, first apron 179M, second apron 190M, hard cap 205M). Spread on purpose: one
    /// team above the second apron, one between the aprons, one mid-tax, one between cap and tax,
    /// one sitting exactly on the soft cap, one with room underneath it. A cap sheet that has only
    /// ever been looked at for a comfortable team has not been tested.
    /// </summary>
    private static readonly long[] TeamPayrollTargets =
    [
        198_000_000,
        181_500_000,
        175_000_000,
        168_000_000,
        141_000_000,
        118_400_000,
    ];

    /// <summary>
    /// Guaranteed money owed to a player who is no longer on the roster, included in the target
    /// above. Present from the start so dead money is exercised as a normal charge with no live
    /// contract behind it, rather than as a case bolted on when free agency arrives.
    /// </summary>
    private static readonly long[] TeamDeadMoneyTargets =
    [
        4_800_000,
        0,
        0,
        7_200_000,
        0,
        3_500_000,
    ];

    /// <summary>Released players, so the dead-money line on the cap sheet has a name against it.</summary>
    private static readonly string[] ReleasedPlayerNames =
    [
        "Casimir Vandeleur",
        string.Empty,
        string.Empty,
        "Ondrej Falkenrath",
        string.Empty,
        "Wilder Nkemdirim",
    ];

    /// <summary>The team that takes up a future option, and the one that turns one down.</summary>
    private const int OptionExercisingTeamIndex = 2;
    private const int OptionDecliningTeamIndex = 5;

    /// <summary>
    /// The fixture's ledger timestamps. Fixed start plus a fixed step per entry: the ledger reads
    /// as a sequence of separate decisions and is still identical on every launch.
    /// </summary>
    private static readonly DateTimeOffset LedgerStart = new(2031, 7, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan LedgerStep = TimeSpan.FromMinutes(37);

    private readonly string _rulesetFilePath;
    private readonly LeagueRulesetSerializer _serializer = new();

    public FixtureLeagueDataSource()
        : this(DefaultRulesetFilePath)
    {
    }

    public FixtureLeagueDataSource(string rulesetFilePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rulesetFilePath);
        _rulesetFilePath = rulesetFilePath;
    }

    /// <summary>
    /// The ruleset shipped alongside the binaries. Copied to the output directory by
    /// <c>BallGM.Infrastructure.csproj</c>, so the client and the tests read the same file.
    /// </summary>
    public static string DefaultRulesetFilePath =>
        Path.Combine(AppContext.BaseDirectory, "data", "rulesets", "default-league.json");

    public DomainOperationResult<LeagueSnapshot> Load()
    {
        string json;
        try
        {
            json = File.ReadAllText(_rulesetFilePath);
        }
        catch (FileNotFoundException)
        {
            return Fail(MissingRulesetFileCode, $"No league ruleset file was found at '{_rulesetFilePath}'.");
        }
        catch (DirectoryNotFoundException)
        {
            return Fail(MissingRulesetFileCode, $"No league ruleset file was found at '{_rulesetFilePath}'.");
        }
        catch (IOException exception)
        {
            return Fail(UnreadableRulesetFileCode, $"The league ruleset file at '{_rulesetFilePath}' could not be read: {exception.Message}");
        }
        catch (UnauthorizedAccessException exception)
        {
            return Fail(UnreadableRulesetFileCode, $"The league ruleset file at '{_rulesetFilePath}' could not be read: {exception.Message}");
        }

        if (string.IsNullOrWhiteSpace(json))
        {
            return Fail(UnreadableRulesetFileCode, $"The league ruleset file at '{_rulesetFilePath}' is empty.");
        }

        var rulesetResult = _serializer.Deserialize(json);
        if (rulesetResult.IsFailure)
        {
            return DomainOperationResult<LeagueSnapshot>.Failure(rulesetResult.Errors.ToArray());
        }

        return BuildLeague(rulesetResult.Value);
    }

    private static DomainOperationResult<LeagueSnapshot> BuildLeague(LeagueRuleset ruleset)
    {
        var maximumRosterSize = ruleset.RosterLimits.MaximumPlayers;
        var franchises = new List<Franchise>();
        var teams = new List<Team>();
        var players = new List<Player>();
        var contracts = new List<Contract>();
        var errors = new List<DomainError>();
        var ledger = new TransactionLedger(new SteppingClock(LedgerStart, LedgerStep));

        for (var teamIndex = 0; teamIndex < FranchiseNames.Length; teamIndex++)
        {
            var franchiseResult = Franchise.Create(
                new FranchiseId(SortableId.NewId()),
                FranchiseNames[teamIndex]);

            if (franchiseResult.IsFailure)
            {
                errors.AddRange(franchiseResult.Errors);
                continue;
            }

            // Each team fills its roster to a different depth, so the league contains both a team
            // with nowhere to put anyone and a team carrying holds for spots it has to fill.
            var rosterSize = Math.Max(1, maximumRosterSize - TeamOpenRosterSpots[teamIndex]);
            var rosterIds = new List<PlayerId>(rosterSize);
            var roster = new List<Player>(rosterSize);
            for (var slot = 0; slot < rosterSize; slot++)
            {
                var playerResult = CreatePlayer(teamIndex, slot, (teamIndex * maximumRosterSize) + slot);
                if (playerResult.IsFailure)
                {
                    errors.AddRange(playerResult.Errors);
                    continue;
                }

                players.Add(playerResult.Value);
                roster.Add(playerResult.Value);
                rosterIds.Add(playerResult.Value.Id);
            }

            var teamResult = Team.Create(
                new TeamId(SortableId.NewId()),
                franchiseResult.Value.Id,
                $"{TeamCities[teamIndex]} {TeamNicknames[teamIndex]}",
                ruleset.RosterLimits,
                rosterIds);

            if (teamResult.IsFailure)
            {
                errors.AddRange(teamResult.Errors);
                continue;
            }

            franchises.Add(franchiseResult.Value);
            teams.Add(teamResult.Value);

            var payrollResult = BuildTeamPayroll(teamIndex, teamResult.Value, roster, ledger);
            if (payrollResult.IsFailure)
            {
                errors.AddRange(payrollResult.Errors);
                continue;
            }

            contracts.AddRange(payrollResult.Value.Contracts);
            players.AddRange(payrollResult.Value.ReleasedPlayers);
        }

        var freeAgentsResult = CreateFreeAgents();
        if (freeAgentsResult.IsFailure)
        {
            errors.AddRange(freeAgentsResult.Errors);
        }
        else
        {
            players.AddRange(freeAgentsResult.Value);
        }

        if (errors.Count > 0)
        {
            return DomainOperationResult<LeagueSnapshot>.Failure(errors.ToArray());
        }

        var leagueResult = League.Create(
            new LeagueId(SortableId.NewId()),
            ruleset.Name,
            teams.Select(team => team.Id));

        if (leagueResult.IsFailure)
        {
            return DomainOperationResult<LeagueSnapshot>.Failure(leagueResult.Errors.ToArray());
        }

        var draftAssetsResult = FixtureDraftAssets.Build(
            leagueResult.Value.Id,
            FixtureSeason,
            franchises,
            ruleset.DraftRules,
            ledger);

        if (draftAssetsResult.IsFailure)
        {
            return DomainOperationResult<LeagueSnapshot>.Failure(draftAssetsResult.Errors.ToArray());
        }

        var configuration = new LeagueConfiguration(
            ruleset.Name,
            ruleset.RegularSeasonGameCount,
            ruleset.RosterLimits,
            ruleset.CapThresholds.PayrollFloor,
            ruleset.CapThresholds.SoftCap,
            ruleset.CapThresholds.LuxuryTax,
            ruleset.CapThresholds.FirstApron,
            ruleset.CapThresholds.SecondApron,
            ruleset.CapThresholds.HardCap,
            ruleset.DraftRules.RoundCount,
            ruleset.DraftRules.LotteryEnabled,
            ruleset.DraftRules.TradableFutureDraftHorizon,
            ruleset.DraftRules.RetainedRoundNumber,
            ruleset.DraftRules.RetainedRoundInterval,
            ruleset.TradeRules.SalaryMatchPercent,
            ruleset.TradeRules.SalaryMatchAllowance,
            ruleset.TradeRules.InjuredPlayerEligibility,
            ruleset.TradeRules.SecondApronBlocksSalaryIncrease,
            new NegotiationConfiguration(
                ruleset.NegotiationRules.MaximumContractSeasons,
                ruleset.NegotiationRules.MaximumIncumbentContractSeasons,
                ruleset.NegotiationRules.MaximumAnnualEscalationPercent,
                ruleset.NegotiationRules.MaximumAnnualDeescalationPercent,
                ruleset.NegotiationRules.CompensationCeiling.Scale,
                ruleset.NegotiationRules.CompensationFloor.Scale,
                ruleset.NegotiationRules.StandardOverCapAllowance,
                ruleset.NegotiationRules.StandardOverCapAllowanceUnavailableAbove,
                ruleset.NegotiationRules.AllowanceMaySplitAcrossPlayers,
                ruleset.NegotiationRules.MarketResolution,
                ruleset.NegotiationRules.OfferExpiryDays));

        return DomainOperationResult<LeagueSnapshot>.Success(
            new LeagueSnapshot(
                leagueResult.Value,
                FixtureSeason,
                franchises,
                teams,
                players,
                contracts,
                draftAssetsResult.Value,
                ledger,
                configuration));
    }

    /// <summary>
    /// Gives one team a payroll: a contract for every player on the roster, plus — where the spread
    /// calls for it — a released player whose guaranteed money is still owed. Every contract and
    /// every release is written to the transaction ledger as it happens, so no figure on the cap
    /// sheet exists without a line explaining where it came from.
    /// </summary>
    private static DomainOperationResult<TeamPayroll> BuildTeamPayroll(
        int teamIndex,
        Team team,
        IReadOnlyList<Player> roster,
        TransactionLedger ledger)
    {
        var deadMoneyTarget = TeamDeadMoneyTargets[teamIndex];
        var activeSalaryTarget = TeamPayrollTargets[teamIndex] - deadMoneyTarget;
        var salaries = DistributeSalary(activeSalaryTarget, roster);

        var contracts = new List<Contract>(roster.Count + 1);
        var releasedPlayers = new List<Player>();
        var errors = new List<DomainError>();

        for (var slot = 0; slot < roster.Count; slot++)
        {
            var player = roster[slot];
            var contractResult = CreateContract(team.Id, player.Id, slot, salaries[slot]);
            if (contractResult.IsFailure)
            {
                errors.AddRange(contractResult.Errors);
                continue;
            }

            var contract = contractResult.Value;
            contracts.Add(contract);

            ledger.Record(
                TransactionKind.ContractSigned,
                FixtureSeason,
                team.Id,
                $"{player.FullName} signed a {contract.Terms.Count}-season contract.",
                player.Id,
                contract.Id,
                new Money(salaries[slot]));
        }

        var optionOutcome = ApplyScriptedOptionDecision(teamIndex, team, roster, contracts, ledger);
        if (optionOutcome.IsFailure)
        {
            errors.AddRange(optionOutcome.Errors);
        }

        if (deadMoneyTarget > 0)
        {
            var deadMoneyResult = CreateDeadMoney(teamIndex, team, deadMoneyTarget, ledger);
            if (deadMoneyResult.IsFailure)
            {
                errors.AddRange(deadMoneyResult.Errors);
            }
            else
            {
                contracts.Add(deadMoneyResult.Value.Contract);
                releasedPlayers.Add(deadMoneyResult.Value.Player);
            }
        }

        return errors.Count > 0
            ? DomainOperationResult<TeamPayroll>.Failure(errors.ToArray())
            : DomainOperationResult<TeamPayroll>.Success(new TeamPayroll(contracts, releasedPlayers));
    }

    /// <summary>
    /// Splits a target payroll across a roster by playing strength, in integer smallest units. The
    /// rounding remainder lands on the best-paid player so the team's total is the target exactly —
    /// a cap sheet that is a few units off its own contracts is a cap sheet nobody can trust.
    /// </summary>
    private static long[] DistributeSalary(long activeSalaryTarget, IReadOnlyList<Player> roster)
    {
        var weights = roster
            .Select(player => (long)Math.Max(1, player.Rating.Overall - 40))
            .ToArray();

        var weightTotal = weights.Sum();
        var salaries = weights.Select(weight => activeSalaryTarget * weight / weightTotal).ToArray();
        salaries[0] += activeSalaryTarget - salaries.Sum();

        return salaries;
    }

    /// <summary>
    /// One contract, running two to four seasons from the current one with modest raises. Guarantee
    /// structure varies by roster slot — fully guaranteed, half guaranteed, or unguaranteed after
    /// the first season — and some deals end on an undecided team or player option, which is what
    /// makes an option year visibly <em>not</em> a cap charge until somebody takes it up.
    /// </summary>
    private static DomainOperationResult<Contract> CreateContract(
        TeamId teamId,
        PlayerId playerId,
        int slot,
        long firstSeasonSalary)
    {
        var seasonCount = 2 + (slot % 3);
        var terms = new List<ContractSeasonTerm>(seasonCount);

        for (var offset = 0; offset < seasonCount; offset++)
        {
            var isFinalSeason = offset == seasonCount - 1;
            var option = isFinalSeason ? OptionForSlot(slot) : ContractOptionKind.None;

            // 5% per season, in integer units — no floating point anywhere near contract money.
            var compensation = new Money(firstSeasonSalary + (firstSeasonSalary * 5 * offset / 100));
            var guaranteed = GuaranteeFor(slot, offset, option, compensation);

            terms.Add(new ContractSeasonTerm(new Season(FixtureSeason.Year + offset), compensation, guaranteed, option));
        }

        return Contract.Create(new ContractId(SortableId.NewId()), teamId, playerId, terms);
    }

    private static ContractOptionKind OptionForSlot(int slot) => slot switch
    {
        _ when slot % 5 == 0 => ContractOptionKind.Team,
        _ when slot % 7 == 3 => ContractOptionKind.Player,
        _ => ContractOptionKind.None,
    };

    private static Money GuaranteeFor(int slot, int seasonOffset, ContractOptionKind option, Money compensation) =>
        (slot % 4, seasonOffset, option) switch
        {
            // An undecided option season is not money owed yet, so nothing about it is guaranteed.
            (_, _, not ContractOptionKind.None) => Money.Zero,

            // The season in progress is always owed in full.
            (_, 0, _) => compensation,

            (1, _, _) => new Money(compensation.SmallestUnits / 2),
            (2, _, _) => Money.Zero,
            _ => compensation,
        };

    /// <summary>
    /// Takes up one team's option and turns another's down, so both decisions exist in the shipped
    /// fixture rather than only in tests. Neither moves the current season's payroll: the option
    /// seasons are in the future, which is precisely the point.
    /// </summary>
    private static DomainOperationResult ApplyScriptedOptionDecision(
        int teamIndex,
        Team team,
        IReadOnlyList<Player> roster,
        IReadOnlyList<Contract> contracts,
        TransactionLedger ledger)
    {
        if (teamIndex != OptionExercisingTeamIndex && teamIndex != OptionDecliningTeamIndex)
        {
            return DomainOperationResult.Success;
        }

        var contract = contracts.FirstOrDefault(candidate =>
            candidate.Terms[^1].Option != ContractOptionKind.None && candidate.Terms[^1].IsPendingOption);

        if (contract is null)
        {
            return DomainOperationResult.Success;
        }

        var optionSeason = contract.Terms[^1].Season;
        var exercising = teamIndex == OptionExercisingTeamIndex;

        var decision = exercising
            ? contract.ExerciseOption(optionSeason)
            : contract.DeclineOption(optionSeason);

        if (decision.IsFailure)
        {
            return decision;
        }

        var playerName = roster.FirstOrDefault(player => player.Id == contract.PlayerId)?.FullName ?? "A player";
        ledger.Record(
            exercising ? TransactionKind.OptionExercised : TransactionKind.OptionDeclined,
            FixtureSeason,
            team.Id,
            exercising
                ? $"The season {optionSeason.Year} option on {playerName}'s contract was exercised."
                : $"The season {optionSeason.Year} option on {playerName}'s contract was declined; the contract ends after season {optionSeason.Year - 1}.",
            contract.PlayerId,
            contract.Id);

        return DomainOperationResult.Success;
    }

    /// <summary>
    /// Signs and then releases a player, leaving exactly <paramref name="deadMoneyTarget"/> of
    /// guaranteed money on the team's books. The released player stays in the loaded player list —
    /// they are off the roster but not off the payroll, which is the whole point of dead money.
    /// </summary>
    private static DomainOperationResult<ReleasedContract> CreateDeadMoney(
        int teamIndex,
        Team team,
        long deadMoneyTarget,
        TransactionLedger ledger)
    {
        var playerResult = Player.Create(
            new PlayerId(SortableId.NewId()),
            ReleasedPlayerNames[teamIndex],
            Position.PowerForward,
            new PlayerRating(58),
            BirthDateForAge(31, teamIndex),
            seasonsOfService: 9);

        if (playerResult.IsFailure)
        {
            return DomainOperationResult<ReleasedContract>.Failure(playerResult.Errors.ToArray());
        }

        var player = playerResult.Value;

        // Half-guaranteed money over two seasons: releasing the player wipes the unguaranteed part
        // and leaves the guarantee behind, which is the dead-money charge the cap sheet shows.
        var compensation = new Money(deadMoneyTarget * 2);
        var contractResult = Contract.Create(
            new ContractId(SortableId.NewId()),
            team.Id,
            player.Id,
            [
                new ContractSeasonTerm(FixtureSeason, compensation, new Money(deadMoneyTarget)),
                new ContractSeasonTerm(new Season(FixtureSeason.Year + 1), compensation, Money.Zero),
            ]);

        if (contractResult.IsFailure)
        {
            return DomainOperationResult<ReleasedContract>.Failure(contractResult.Errors.ToArray());
        }

        var contract = contractResult.Value;
        ledger.Record(
            TransactionKind.ContractSigned,
            FixtureSeason,
            team.Id,
            $"{player.FullName} signed a 2-season contract.",
            player.Id,
            contract.Id,
            compensation);

        var terminateResult = contract.Terminate(FixtureSeason);
        if (terminateResult.IsFailure)
        {
            return DomainOperationResult<ReleasedContract>.Failure(terminateResult.Errors.ToArray());
        }

        ledger.Record(
            TransactionKind.PlayerReleased,
            FixtureSeason,
            team.Id,
            $"{player.FullName} was released. The guaranteed part of the contract stays on the books as dead money.",
            player.Id,
            contract.Id,
            new Money(deadMoneyTarget));

        return DomainOperationResult<ReleasedContract>.Success(new ReleasedContract(player, contract));
    }

    private sealed record TeamPayroll(IReadOnlyList<Contract> Contracts, IReadOnlyList<Player> ReleasedPlayers);

    private sealed record ReleasedContract(Player Player, Contract Contract);

    /// <summary>
    /// The unsigned players. They are loaded exactly like anyone else and simply appear on no
    /// roster and under no contract, which is what makes them free agents — there is no separate
    /// pool, because a second collection of people is a second place a player can be forgotten.
    /// </summary>
    private static DomainOperationResult<IReadOnlyList<Player>> CreateFreeAgents()
    {
        var created = new List<Player>(FreeAgents.Length);
        var errors = new List<DomainError>();

        for (var index = 0; index < FreeAgents.Length; index++)
        {
            var plan = FreeAgents[index];

            var playerResult = Player.Create(
                new PlayerId(SortableId.NewId()),
                plan.FullName,
                plan.Position,
                new PlayerRating(plan.Overall),
                // The first of the season's year, rather than the spread of birthdays the rosters
                // carry. Age is read at a fixed point in the season, so a free agent whose birthday
                // falls later in the year would show up a year younger than the plan says — and a
                // market fixture whose stated ages are not the ages on screen is a fixture that
                // cannot be reasoned about. Rostered players keep varied dates; these nine do not.
                new DateOnly(FixtureSeason.Year - plan.Age, 1, 1),
                plan.SeasonsOfService,
                plan.Injury is null ? null : new Injury(plan.Injury));

            if (playerResult.IsFailure)
            {
                errors.AddRange(playerResult.Errors);
                continue;
            }

            created.Add(playerResult.Value);
        }

        return errors.Count > 0
            ? DomainOperationResult<IReadOnlyList<Player>>.Failure(errors.ToArray())
            : DomainOperationResult<IReadOnlyList<Player>>.Success(created);
    }

    private static DomainOperationResult<Player> CreatePlayer(int teamIndex, int slot, int leagueWidePlayerIndex)
    {
        // Both name lists are stepped by strides coprime with their length and shifted once per lap
        // through the list. Walking the given names in order made every team's roster open with the
        // same first names in the same sequence, which is instantly readable as generated content.
        // The pair (given, surname) stays unique for far more players than this fixture creates.
        var lap = leagueWidePlayerIndex / GivenNames.Length;
        var givenName = GivenNames[((leagueWidePlayerIndex * 11) + (lap * 7)) % GivenNames.Length];
        var surname = Surnames[(leagueWidePlayerIndex + (lap * 13)) % Surnames.Length];
        var fullName = $"{givenName} {surname}";

        var strength = teamIndex < TeamStrengthOffsets.Length ? TeamStrengthOffsets[teamIndex] : 0;

        // A deterministic wobble, so two teams with the same strength offset are not rating-identical.
        var wobble = (((teamIndex * 7) + (slot * 13)) % 5) - 2;
        var overall = Math.Clamp(
            OverallForSlot(slot) + strength + wobble,
            PlayerRating.MinimumOverall + 30,
            PlayerRating.MaximumOverall);

        var injury = ScriptedInjuries.TryGetValue((teamIndex, slot), out var description)
            ? new Injury(description)
            : null;

        // A spread of ages and service across every roster, deterministic like the rest of the
        // fixture. Rookies and veterans have to both exist for a tiered compensation rule to have
        // anything to key off, and a league of identically aged players would hide that entirely.
        var age = 19 + (((teamIndex * 3) + (slot * 5)) % 17);
        var seasonsOfService = Math.Max(0, age - 19 - ((teamIndex + slot) % 2));

        return Player.Create(
            new PlayerId(SortableId.NewId()),
            fullName,
            RosterPositionPlan[(slot + (teamIndex * 2)) % RosterPositionPlan.Length],
            new PlayerRating(overall),
            BirthDateForAge(age, leagueWidePlayerIndex),
            seasonsOfService,
            injury);
    }

    /// <summary>
    /// A birth date that makes a player exactly <paramref name="age"/> during the fixture season.
    /// Fixed, not random, so the same league appears on every launch — and expressed as a date
    /// rather than a stored age, because an age stops being true the moment the calendar moves.
    /// </summary>
    private static DateOnly BirthDateForAge(int age, int index) =>
        new(FixtureSeason.Year - age, ((index * 7) % 12) + 1, ((index * 11) % 28) + 1);

    /// <summary>
    /// The curve for rosters the plan covers; below it, each further slot drops two more points, so
    /// a ruleset file asking for a bigger roster still produces a sane tail instead of an exception.
    /// </summary>
    private static int OverallForSlot(int slot) =>
        slot < SlotOverallCurve.Length
            ? SlotOverallCurve[slot]
            : SlotOverallCurve[^1] - (2 * (slot - SlotOverallCurve.Length + 1));

    private static DomainOperationResult<LeagueSnapshot> Fail(string code, string message) =>
        DomainOperationResult<LeagueSnapshot>.Failure(new DomainError(code, message));
}
