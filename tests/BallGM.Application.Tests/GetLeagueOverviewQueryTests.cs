using BallGM.Application.Cap;
using BallGM.Application.DraftAssets;
using BallGM.Application.Leagues;
using BallGM.Domain.Cap;
using BallGM.Domain.Common;
using BallGM.Domain.Contracts;
using BallGM.Domain.DraftAssets;
using BallGM.Domain.Franchises;
using BallGM.Domain.Leagues;
using BallGM.Domain.Players;
using BallGM.Domain.Teams;
using BallGM.Domain.Trades;
using BallGM.Domain.Transactions;

namespace BallGM.Application.Tests;

public sealed class GetLeagueOverviewQueryTests
{
    [Fact]
    public void Overview_ReportsEachLeagueTeamWithItsFranchiseAndRoster()
    {
        var league = new LeagueBuilder()
            .WithTeam("Harbourline Tidewatch", "Harbourline Basketball Club", playerCount: 3)
            .WithTeam("Verdanmoor Kestrels", "Verdanmoor Athletic", playerCount: 2)
            .Build();

        var result = new GetLeagueOverviewQuery(league, new StubCapLedger(), new StubDraftAssetLedger()).Execute();

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Teams.Count);

        var tidewatch = result.Value.Teams.Single(team => team.TeamName == "Harbourline Tidewatch");
        Assert.Equal("Harbourline Basketball Club", tidewatch.FranchiseName);
        Assert.Equal(3, tidewatch.RosterCount);
        Assert.Equal(3, tidewatch.Roster.Count);
    }

    [Fact]
    public void Overview_CarriesRulesetLimitsAndCapThresholdsFromTheLoadedConfiguration()
    {
        var league = new LeagueBuilder()
            .WithTeam("Harbourline Tidewatch", "Harbourline Basketball Club", playerCount: 2)
            .Build();

        var result = new GetLeagueOverviewQuery(league, new StubCapLedger(), new StubDraftAssetLedger()).Execute();

        Assert.True(result.IsSuccess);
        Assert.Equal("Test Ruleset", result.Value.RulesetName);
        Assert.Equal(78, result.Value.RegularSeasonGameCount);
        Assert.Equal(1, result.Value.MinimumRosterPlayers);
        Assert.Equal(15, result.Value.MaximumRosterPlayers);
        Assert.Equal(141_000_000, result.Value.CapThresholds.SoftCap);
        Assert.Equal(205_000_000, result.Value.CapThresholds.HardCap);
    }

    [Fact]
    public void Teams_AreListedByNameSoTheOrderDoesNotDependOnMintedIdentifiers()
    {
        var league = new LeagueBuilder()
            .WithTeam("Verdanmoor Kestrels", "Verdanmoor Athletic", playerCount: 1)
            .WithTeam("Harbourline Tidewatch", "Harbourline Basketball Club", playerCount: 1)
            .WithTeam("Northreach Aurora", "Northreach Athletic Union", playerCount: 1)
            .Build();

        var result = new GetLeagueOverviewQuery(league, new StubCapLedger(), new StubDraftAssetLedger()).Execute();

        Assert.True(result.IsSuccess);
        Assert.Equal(
            ["Harbourline Tidewatch", "Northreach Aurora", "Verdanmoor Kestrels"],
            result.Value.Teams.Select(team => team.TeamName));
    }

    [Fact]
    public void Roster_ListsHighestRatedPlayerFirst()
    {
        var league = new LeagueBuilder()
            .WithTeam(
                "Harbourline Tidewatch",
                "Harbourline Basketball Club",
                ("Bench Body", Position.Center, 61, null),
                ("Star Wing", Position.SmallForward, 88, null),
                ("Rotation Guard", Position.PointGuard, 74, null))
            .Build();

        var result = new GetLeagueOverviewQuery(league, new StubCapLedger(), new StubDraftAssetLedger()).Execute();

        Assert.True(result.IsSuccess);
        Assert.Equal(
            ["Star Wing", "Rotation Guard", "Bench Body"],
            result.Value.Teams.Single().Roster.Select(spot => spot.FullName));
    }

    [Fact]
    public void Roster_SurfacesInjuryStatusAndAbbreviatedPosition()
    {
        var league = new LeagueBuilder()
            .WithTeam(
                "Harbourline Tidewatch",
                "Harbourline Basketball Club",
                ("Healthy Centre", Position.Center, 70, null),
                ("Hurt Guard", Position.ShootingGuard, 80, "Sprained left ankle"))
            .Build();

        var result = new GetLeagueOverviewQuery(league, new StubCapLedger(), new StubDraftAssetLedger()).Execute();

        Assert.True(result.IsSuccess);
        var roster = result.Value.Teams.Single().Roster;

        var hurt = roster.Single(spot => spot.FullName == "Hurt Guard");
        Assert.True(hurt.IsInjured);
        Assert.Equal("Sprained left ankle", hurt.InjuryDescription);
        Assert.Equal("SG", hurt.Position);

        var healthy = roster.Single(spot => spot.FullName == "Healthy Centre");
        Assert.False(healthy.IsInjured);
        Assert.Null(healthy.InjuryDescription);
        Assert.Equal("C", healthy.Position);
    }

    [Fact]
    public void Overview_ExplainsWhenTheLeagueReferencesATeamThatWasNotLoaded()
    {
        var league = new LeagueBuilder()
            .WithTeam("Harbourline Tidewatch", "Harbourline Basketball Club", playerCount: 2)
            .WithDanglingTeamReference("MISSING-TEAM")
            .Build();

        var result = new GetLeagueOverviewQuery(league, new StubCapLedger(), new StubDraftAssetLedger()).Execute();

        Assert.True(result.IsFailure);
        var error = Assert.Single(result.Errors);
        Assert.Equal("league_overview.unknown_team", error.Code);
        Assert.Contains("MISSING-TEAM", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Overview_ExplainsWhenATeamReferencesAPlayerThatWasNotLoaded()
    {
        var league = new LeagueBuilder()
            .WithTeam("Harbourline Tidewatch", "Harbourline Basketball Club", playerCount: 2)
            .WithoutLoadingPlayers()
            .Build();

        var result = new GetLeagueOverviewQuery(league, new StubCapLedger(), new StubDraftAssetLedger()).Execute();

        Assert.True(result.IsFailure);
        Assert.All(result.Errors, error => Assert.Equal("league_overview.unknown_player", error.Code));
    }

    [Fact]
    public void Overview_ExplainsWhenATeamReferencesAFranchiseThatWasNotLoaded()
    {
        var league = new LeagueBuilder()
            .WithTeam("Harbourline Tidewatch", "Harbourline Basketball Club", playerCount: 2)
            .WithoutLoadingFranchises()
            .Build();

        var result = new GetLeagueOverviewQuery(league, new StubCapLedger(), new StubDraftAssetLedger()).Execute();

        Assert.True(result.IsFailure);
        var error = Assert.Single(result.Errors);
        Assert.Equal("league_overview.unknown_franchise", error.Code);
    }

    [Fact]
    public void Overview_PropagatesTheDataSourceFailureUnchanged()
    {
        var failure = DomainOperationResult<LeagueSnapshot>.Failure(
            new DomainError("ruleset.malformed_file", "The league ruleset file is not valid JSON."));

        var result = new GetLeagueOverviewQuery(new StubLeagueDataSource(failure), new StubCapLedger(), new StubDraftAssetLedger()).Execute();

        Assert.True(result.IsFailure);
        var error = Assert.Single(result.Errors);
        Assert.Equal("ruleset.malformed_file", error.Code);
    }

    [Fact]
    public void CapSheet_TotalsTheChargesTheTeamsOwnContractsProduce()
    {
        var league = new LeagueBuilder()
            .WithTeam(
                "Harbourline Tidewatch",
                "Harbourline Basketball Club",
                ("Star Wing", Position.SmallForward, 88, null),
                ("Rotation Guard", Position.PointGuard, 74, null))
            .Build();

        var result = new GetLeagueOverviewQuery(league, new StubCapLedger(), new StubDraftAssetLedger()).Execute();

        Assert.True(result.IsSuccess);
        var capSheet = result.Value.Teams.Single().CapSheet;

        Assert.Equal(LeagueBuilder.CurrentSeason.Year, capSheet.SeasonYear);
        Assert.Equal((88 + 74) * LeagueBuilder.SalaryPerOverallPoint, capSheet.CommittedSalary);
        Assert.Equal(0, capSheet.DeadMoney);
        Assert.Equal(capSheet.CommittedSalary, capSheet.TotalPayroll);
        Assert.Equal(["Star Wing", "Rotation Guard"], capSheet.Charges.Select(charge => charge.PlayerName));
    }

    [Fact]
    public void CapSheet_KeepsDeadMoneyOnTheBooksAndOffTheRoster()
    {
        var league = new LeagueBuilder()
            .WithTeam("Harbourline Tidewatch", "Harbourline Basketball Club", playerCount: 2)
            .WithReleasedPlayer("Casimir Vandeleur", guaranteedAmount: 6_200_000)
            .Build();

        var result = new GetLeagueOverviewQuery(league, new StubCapLedger(), new StubDraftAssetLedger()).Execute();

        Assert.True(result.IsSuccess);
        var team = result.Value.Teams.Single();

        Assert.Equal(6_200_000, team.CapSheet.DeadMoney);
        Assert.Equal(team.CapSheet.CommittedSalary + 6_200_000, team.CapSheet.TotalPayroll);

        var deadMoneyLine = Assert.Single(team.CapSheet.Charges, charge => charge.IsDeadMoney);
        Assert.Equal("Casimir Vandeleur", deadMoneyLine.PlayerName);
        Assert.DoesNotContain(team.Roster, spot => spot.FullName == "Casimir Vandeleur");
    }

    [Fact]
    public void CapSheet_CarriesTheLedgersRuleCodeAndExplanationThroughToTheReadModel()
    {
        var league = new LeagueBuilder()
            .WithTeam("Harbourline Tidewatch", "Harbourline Basketball Club", playerCount: 2)
            .Build();

        var result = new GetLeagueOverviewQuery(league, new StubCapLedger(), new StubDraftAssetLedger()).Execute();

        Assert.True(result.IsSuccess);
        var standing = Assert.Single(result.Value.Teams.Single().CapSheet.Thresholds);

        Assert.Equal("Soft cap", standing.ThresholdName);
        Assert.Equal("cap.under_soft_cap", standing.RuleCode);
        Assert.False(standing.IsOver);
        Assert.Equal("Test explanation.", standing.Explanation);
    }

    [Fact]
    public void CapSheet_ShowsTheLedgerEntriesBehindThePayrollNewestFirst()
    {
        var league = new LeagueBuilder()
            .WithTeam("Harbourline Tidewatch", "Harbourline Basketball Club", playerCount: 2)
            .WithReleasedPlayer("Casimir Vandeleur", guaranteedAmount: 1_000_000)
            .Build();

        var result = new GetLeagueOverviewQuery(league, new StubCapLedger(), new StubDraftAssetLedger()).Execute();

        Assert.True(result.IsSuccess);
        var transactions = result.Value.Teams.Single().CapSheet.Transactions;

        Assert.Equal("Player released", transactions[0].Kind);
        Assert.Equal(1_000_000, transactions[0].Amount);
        Assert.All(transactions.Skip(1), line => Assert.Equal("Contract signed", line.Kind));
    }

    [Fact]
    public void Roster_ShowsWhatEachPlayerCostsAgainstTheCurrentSeason()
    {
        var league = new LeagueBuilder()
            .WithTeam(
                "Harbourline Tidewatch",
                "Harbourline Basketball Club",
                ("Star Wing", Position.SmallForward, 88, null))
            .Build();

        var result = new GetLeagueOverviewQuery(league, new StubCapLedger(), new StubDraftAssetLedger()).Execute();

        Assert.True(result.IsSuccess);
        var spot = result.Value.Teams.Single().Roster.Single();

        Assert.Equal(88 * LeagueBuilder.SalaryPerOverallPoint, spot.CapCharge);
        Assert.Equal(1, spot.ContractSeasonsRemaining);
    }

    [Fact]
    public void Overview_ExplainsItselfWhenTheCapLedgerRejectsATeamsCharges()
    {
        var league = new LeagueBuilder()
            .WithTeam("Harbourline Tidewatch", "Harbourline Basketball Club", playerCount: 2)
            .Build();

        var result = new GetLeagueOverviewQuery(league, new FailingCapLedger(), new StubDraftAssetLedger()).Execute();

        Assert.True(result.IsFailure);
        Assert.Equal("cap_ledger.charge_team_mismatch", Assert.Single(result.Errors).Code);
    }

    private sealed class StubLeagueDataSource(DomainOperationResult<LeagueSnapshot> result) : ILeagueDataSource
    {
        public DomainOperationResult<LeagueSnapshot> Load() => result;
    }

    [Fact]
    public void PickBoard_StartsAtTheNextDraftBecauseTheCurrentOneHasAlreadyBeenSettled()
    {
        var league = new LeagueBuilder()
            .WithTeam("Harbourline Tidewatch", "Harbourline Basketball Club", playerCount: 2)
            .Build();

        var result = new GetLeagueOverviewQuery(league, new StubCapLedger(), new StubDraftAssetLedger()).Execute();

        Assert.True(result.IsSuccess);
        Assert.Equal(LeagueBuilder.CurrentSeason.Year + 1, result.Value.PickBoard.FirstDraftSeason);
        Assert.Equal(LeagueBuilder.DraftHorizon, result.Value.PickBoard.DraftCount);
        Assert.Equal(
            [LeagueBuilder.CurrentSeason.Year + 1, LeagueBuilder.CurrentSeason.Year + 2],
            result.Value.PickBoard.DraftSeasons);
    }

    [Fact]
    public void PickBoard_ShowsAnOwedPickWithItsProtectionAndTheFranchiseItIsOwedTo()
    {
        var league = new LeagueBuilder()
            .WithTeam("Harbourline Tidewatch", "Harbourline Basketball Club", playerCount: 2)
            .WithTeam("Verdanmoor Kestrels", "Verdanmoor Athletic", playerCount: 2)
            .WithAProtectedPickOwedToTheSecondFranchise()
            .Build();

        var result = new GetLeagueOverviewQuery(league, new StubCapLedger(), new StubDraftAssetLedger()).Execute();

        Assert.True(result.IsSuccess);

        var owedAsset = result.Value.PickBoard.Franchises
            .Single(row => row.FranchiseName == "Harbourline Basketball Club")
            .Drafts
            .Single(cell => cell.DraftSeason == LeagueBuilder.CurrentSeason.Year + 1)
            .Assets
            .Single();

        Assert.Equal("Owed", owedAsset.State);
        Assert.Equal("Harbourline Basketball Club", owedAsset.OriginalFranchiseName);
        Assert.Equal("Verdanmoor Athletic", owedAsset.CounterpartyName);
        Assert.Equal(StubDraftAssetLedger.ProtectionSummary, owedAsset.ProtectionSummary);
        Assert.Equal(StubDraftAssetLedger.HeldOutcome, owedAsset.OutcomeIfProtectionHolds);
    }

    [Fact]
    public void PickBoard_HangsThatAssetsOwnLedgerHistoryOffItForTheDrillDown()
    {
        var league = new LeagueBuilder()
            .WithTeam("Harbourline Tidewatch", "Harbourline Basketball Club", playerCount: 2)
            .WithTeam("Verdanmoor Kestrels", "Verdanmoor Athletic", playerCount: 2)
            .WithAProtectedPickOwedToTheSecondFranchise()
            .Build();

        var result = new GetLeagueOverviewQuery(league, new StubCapLedger(), new StubDraftAssetLedger()).Execute();

        Assert.True(result.IsSuccess);

        var assets = result.Value.PickBoard.Franchises
            .Single(row => row.FranchiseName == "Harbourline Basketball Club")
            .Drafts
            .SelectMany(cell => cell.Assets)
            .ToList();

        var owed = assets.Single(asset => asset.ProtectionSummary is not null);
        var history = Assert.Single(owed.History);
        Assert.Equal("Pick encumbered", history.Kind);
        Assert.Equal(LeagueBuilder.OwedPickReason, history.Reason);

        // The franchise's other future pick has no history of its own: an asset nothing has happened
        // to must show an empty trail rather than the league's whole ledger.
        Assert.Empty(assets.Single(asset => asset.ProtectionSummary is null).History);
    }

    /// <summary>
    /// Stands in for the Rules cap ledger, which Application deliberately cannot reference. It adds
    /// the charges up and reports one threshold; how the real ledger compares a payroll to five of
    /// them is tested in <c>BallGM.Rules.Tests</c>, and the two are wired together for real in
    /// <c>BallGM.Integration.Tests</c>.
    /// </summary>
    private sealed class StubCapLedger : ICapLedger
    {
        public DomainOperationResult<TeamCapSheet> Evaluate(
            TeamId teamId,
            Season season,
            IReadOnlyCollection<CapCharge> charges,
            LeagueConfiguration configuration)
        {
            var committed = Money.Sum(charges.Where(charge => !charge.IsDeadMoney).Select(charge => charge.Amount));
            var dead = Money.Sum(charges.Where(charge => charge.IsDeadMoney).Select(charge => charge.Amount));
            var total = committed + dead;

            return DomainOperationResult<TeamCapSheet>.Success(new TeamCapSheet(
                teamId,
                season,
                committed,
                dead,
                total,
                charges.ToList(),
                [
                    new ThresholdStanding(
                        CapThresholdKind.SoftCap,
                        configuration.SoftCap,
                        configuration.SoftCap.SignedDifferenceFrom(total),
                        ThresholdPosition.Under,
                        "cap.under_soft_cap",
                        "Test explanation."),
                ]));
        }
    }

    /// <summary>
    /// Stands in for the Rules draft-asset ledger for the same reason <see cref="StubCapLedger"/>
    /// stands in for the cap one: Application cannot reference Rules. It reports each franchise's own
    /// picks and whether they are owed away; how the real ledger words a protection is tested in
    /// <c>BallGM.Rules.Tests</c>.
    /// </summary>
    private sealed class StubDraftAssetLedger : IDraftAssetLedger
    {
        internal const string ProtectionSummary = "Owed away: protected through selection 4.";
        internal const string HeldOutcome = "If it lands in the top 4, the obligation rolls to the next draft.";

        public DomainOperationResult<DraftAssetBoard> BuildBoard(
            DraftAssetBook book,
            IReadOnlyList<FranchiseDraftIdentity> franchises,
            Season firstDraftSeason,
            LeagueConfiguration configuration)
        {
            var rows = franchises
                .Select(franchise => new DraftAssetBoardRow(
                    franchise.FranchiseId,
                    Enumerable
                        .Range(0, configuration.TradableFutureDraftHorizon)
                        .Select(offset => BuildCell(book, franchise, new Season(firstDraftSeason.Year + offset)))
                        .ToList()))
                .ToList();

            return DomainOperationResult<DraftAssetBoard>.Success(new DraftAssetBoard(
                firstDraftSeason,
                configuration.TradableFutureDraftHorizon,
                configuration.DraftRoundCount,
                rows));
        }

        public DomainOperationResult ValidateTransfer(
            DraftAssetBook book,
            DraftPickId pickId,
            FranchiseId fromFranchiseId,
            FranchiseId toFranchiseId,
            Season currentSeason,
            LeagueConfiguration configuration) =>
            DomainOperationResult.Success;

        private static DraftAssetBoardCell BuildCell(DraftAssetBook book, FranchiseDraftIdentity franchise, Season season)
        {
            var assets = book
                .PicksInDraft(season)
                .Where(pick => pick.OriginalFranchiseId == franchise.FranchiseId)
                .Select(pick =>
                {
                    var ownership = book.Ownership(pick.Id)!;
                    var obligation = ownership.Obligation;

                    return new PickAssetLine(
                        pick.Id,
                        pick.Round,
                        pick.OriginalFranchiseId,
                        ownership.CurrentOwnerFranchiseId,
                        obligation is null ? PickControlState.OwnedOutright : PickControlState.OwedAway,
                        obligation?.BeneficiaryFranchiseId,
                        obligation is null ? null : ProtectionSummary,
                        obligation is null ? null : HeldOutcome);
                })
                .ToList();

            return new DraftAssetBoardCell(season, assets);
        }
    }

    private sealed class FailingCapLedger : ICapLedger
    {
        public DomainOperationResult<TeamCapSheet> Evaluate(
            TeamId teamId,
            Season season,
            IReadOnlyCollection<CapCharge> charges,
            LeagueConfiguration configuration) =>
            DomainOperationResult<TeamCapSheet>.Failure(
                new DomainError("cap_ledger.charge_team_mismatch", "A charge belonging to another team was supplied."));
    }

    private sealed class FixedTestClock(DateTimeOffset instant) : IClock
    {
        public DateTimeOffset UtcNow => instant;
    }

    /// <summary>
    /// Assembles a loaded league in memory so the query is tested without touching the filesystem.
    /// Uses the real aggregate factories, so a test fixture that violates a roster invariant fails
    /// here rather than producing an aggregate the production code could never build.
    /// </summary>
    private sealed class LeagueBuilder
    {
        /// <summary>Salary per rating point, so a roster's payroll follows from its ratings.</summary>
        internal const long SalaryPerOverallPoint = 100_000;

        internal static readonly Season CurrentSeason = new(2031);

        internal const int DraftRoundCount = 1;
        internal const bool DraftLotteryEnabled = true;
        internal const int DraftHorizon = 2;
        internal const int RetainedRoundNumber = 1;
        internal const int RetainedRoundInterval = 2;
        internal const int SalaryMatchPercent = 125;
        internal const long SalaryMatchAllowance = 250_000;
        internal const bool SecondApronBlocksSalaryIncrease = true;
        internal const string OwedPickReason = "The next first-round pick is owed, top-4 protected.";

        private static readonly RosterSizeLimits Limits = new(1, 15);
        private static readonly DateTimeOffset LedgerInstant = new(2031, 7, 1, 9, 0, 0, TimeSpan.Zero);

        private readonly List<Franchise> _franchises = [];
        private readonly List<Team> _teams = [];
        private readonly List<Player> _players = [];
        private readonly List<Contract> _contracts = [];
        private readonly List<TeamId> _extraTeamIds = [];
        private readonly TransactionLedger _ledger = new(new FixedTestClock(LedgerInstant));

        private bool _loadPlayers = true;
        private bool _loadFranchises = true;
        private bool _owesAProtectedPick;

        /// <summary>
        /// Promises the first franchise's next first-round pick to the second, and records the
        /// ledger line behind it, so the board's drill-down has a history to resolve.
        /// </summary>
        public LeagueBuilder WithAProtectedPickOwedToTheSecondFranchise()
        {
            _owesAProtectedPick = true;
            return this;
        }

        public LeagueBuilder WithTeam(string teamName, string franchiseName, int playerCount)
        {
            var roster = Enumerable
                .Range(0, playerCount)
                .Select(index => ($"Player {teamName} {index}", Position.PointGuard, 70 - index, (string?)null))
                .ToArray();

            return WithTeam(teamName, franchiseName, roster);
        }

        public LeagueBuilder WithTeam(
            string teamName,
            string franchiseName,
            params (string FullName, Position Position, int Overall, string? Injury)[] roster)
        {
            var franchise = Unwrap(Franchise.Create(new FranchiseId(SortableId.NewId()), franchiseName));
            var playerIds = new List<PlayerId>();

            foreach (var (fullName, position, overall, injury) in roster)
            {
                var player = Unwrap(Player.Create(
                    new PlayerId(SortableId.NewId()),
                    fullName,
                    position,
                    new PlayerRating(overall),
                    injury is null ? null : new Injury(injury)));

                _players.Add(player);
                playerIds.Add(player.Id);
            }

            var team = Unwrap(Team.Create(
                new TeamId(SortableId.NewId()),
                franchise.Id,
                teamName,
                Limits,
                playerIds));

            _franchises.Add(franchise);
            _teams.Add(team);

            foreach (var player in _players.Where(candidate => playerIds.Contains(candidate.Id)))
            {
                var contract = Unwrap(Contract.Create(
                    new ContractId(SortableId.NewId()),
                    team.Id,
                    player.Id,
                    [
                        new ContractSeasonTerm(
                            CurrentSeason,
                            new Money(player.Rating.Overall * SalaryPerOverallPoint),
                            new Money(player.Rating.Overall * SalaryPerOverallPoint)),
                    ]));

                _contracts.Add(contract);
                _ledger.Record(
                    TransactionKind.ContractSigned,
                    CurrentSeason,
                    team.Id,
                    $"{player.FullName} signed a 1-season contract.",
                    player.Id,
                    contract.Id,
                    new Money(player.Rating.Overall * SalaryPerOverallPoint));
            }

            return this;
        }

        /// <summary>
        /// Signs and releases a player on the most recently added team, leaving
        /// <paramref name="guaranteedAmount"/> of dead money behind — a charge with no live contract
        /// behind it, and a player the roster no longer references.
        /// </summary>
        public LeagueBuilder WithReleasedPlayer(string fullName, long guaranteedAmount)
        {
            var team = _teams[^1];
            var player = Unwrap(Player.Create(
                new PlayerId(SortableId.NewId()),
                fullName,
                Position.PowerForward,
                new PlayerRating(60)));

            var contract = Unwrap(Contract.Create(
                new ContractId(SortableId.NewId()),
                team.Id,
                player.Id,
                [
                    new ContractSeasonTerm(
                        CurrentSeason,
                        new Money(guaranteedAmount * 2),
                        new Money(guaranteedAmount)),
                ]));

            Assert.True(contract.Terminate(CurrentSeason).IsSuccess);

            _players.Add(player);
            _contracts.Add(contract);
            _ledger.Record(
                TransactionKind.PlayerReleased,
                CurrentSeason,
                team.Id,
                $"{fullName} was released.",
                player.Id,
                contract.Id,
                new Money(guaranteedAmount));

            return this;
        }

        public LeagueBuilder WithDanglingTeamReference(string teamId)
        {
            _extraTeamIds.Add(new TeamId(teamId));
            return this;
        }

        public LeagueBuilder WithoutLoadingPlayers()
        {
            _loadPlayers = false;
            return this;
        }

        public LeagueBuilder WithoutLoadingFranchises()
        {
            _loadFranchises = false;
            return this;
        }

        public ILeagueDataSource Build()
        {
            var league = Unwrap(League.Create(
                new LeagueId(SortableId.NewId()),
                "Continental Basketball Association",
                _teams.Select(team => team.Id).Concat(_extraTeamIds)));

            var configuration = new LeagueConfiguration(
                "Test Ruleset",
                78,
                Limits,
                new Money(141_000_000),
                new Money(172_000_000),
                new Money(179_000_000),
                new Money(190_000_000),
                new Money(205_000_000),
                DraftRoundCount,
                DraftLotteryEnabled,
                DraftHorizon,
                RetainedRoundNumber,
                RetainedRoundInterval,
                SalaryMatchPercent,
                new Money(SalaryMatchAllowance),
                InjuredPlayerTradeEligibility.AllowedWithWarning,
                SecondApronBlocksSalaryIncrease);

            var snapshot = new LeagueSnapshot(
                league,
                CurrentSeason,
                _loadFranchises ? _franchises : [],
                _teams,
                _loadPlayers ? _players : [],
                _contracts,
                BuildDraftAssets(league.Id),
                _ledger,
                configuration);

            return new StubLeagueDataSource(DomainOperationResult<LeagueSnapshot>.Success(snapshot));
        }

        /// <summary>
        /// One first-round pick per franchise for each future draft the board covers, plus — when the
        /// test asks for it — a protection riding on the first franchise's next one.
        /// </summary>
        private DraftAssetBook BuildDraftAssets(LeagueId leagueId)
        {
            var book = new DraftAssetBook(leagueId);

            for (var offset = 1; offset <= DraftHorizon; offset++)
            {
                foreach (var franchise in _franchises)
                {
                    var pick = Unwrap(DraftPick.Create(
                        new DraftPickId(SortableId.NewId()),
                        leagueId,
                        new Season(CurrentSeason.Year + offset),
                        round: 1,
                        franchise.Id));

                    Assert.True(book.Register(pick).IsSuccess);
                }
            }

            if (!_owesAProtectedPick || _franchises.Count < 2)
            {
                return book;
            }

            var owedPick = book.Find(new Season(CurrentSeason.Year + 1), 1, _franchises[0].Id)!;
            var protection = Unwrap(PickProtection.TopSelections([4], PickProtectionFallback.Extinguishes));

            Assert.True(book.Encumber(
                owedPick.Id,
                new PickObligation(
                    new PickEncumbranceId(SortableId.NewId()),
                    _franchises[1].Id,
                    protection)).IsSuccess);

            _ledger.RecordPickEvent(
                TransactionKind.DraftPickEncumbered,
                CurrentSeason,
                _franchises[0].Id,
                owedPick.Id,
                OwedPickReason,
                _franchises[1].Id);

            return book;
        }

        private static T Unwrap<T>(DomainOperationResult<T> result)
        {
            Assert.True(result.IsSuccess, string.Join("; ", result.Errors.Select(error => error.Message)));
            return result.Value;
        }
    }
}
