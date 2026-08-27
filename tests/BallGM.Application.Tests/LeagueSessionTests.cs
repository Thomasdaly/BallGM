using BallGM.Application.Cap;
using BallGM.Application.DraftAssets;
using BallGM.Application.Leagues;
using BallGM.Application.Trades;
using BallGM.Domain.Cap;
using BallGM.Domain.Common;
using BallGM.Domain.DraftAssets;
using BallGM.Domain.Franchises;
using BallGM.Domain.Leagues;
using BallGM.Domain.Players;
using BallGM.Domain.Teams;
using BallGM.Domain.Trades;

namespace BallGM.Application.Tests;

/// <summary>
/// The session's own job: hold one league, turn identifiers from a screen into a proposal, and map
/// the rules layer's answer back into something a screen can render. What the rules decide is tested
/// in <c>BallGM.Rules.Tests</c>; the engine here is a stub, so a mapping bug cannot hide behind it.
/// </summary>
public sealed class LeagueSessionTests
{
    [Fact]
    public void Session_ReportsNoLeagueUntilOneIsLoaded()
    {
        var session = NewSession(out _, out _);

        var result = session.Overview();

        Assert.False(session.IsLoaded);
        Assert.True(result.IsFailure);
        Assert.Equal("league_session.not_loaded", Assert.Single(result.Errors).Code);
    }

    [Fact]
    public void AssessTrade_BuildsAProposalFromTheIdentifiersAScreenHolds()
    {
        var session = NewSession(out var engine, out var league);
        Assert.True(session.Load().IsSuccess);

        var request = new TradeRequest(
            [league.FirstTeamId, league.SecondTeamId],
            [
                new TradeAssetRequest(TradeAssetRequest.PlayerKind, league.FirstTeamPlayerId, league.FirstTeamId, league.SecondTeamId),
                new TradeAssetRequest(TradeAssetRequest.PickKind, league.SecondTeamPickId, league.SecondTeamId, league.FirstTeamId),
            ]);

        var result = session.AssessTrade(request);

        Assert.True(result.IsSuccess, string.Join("; ", result.Errors.Select(error => error.Message)));
        Assert.Equal(2, engine.LastProposal!.Movements.Count);
        Assert.Equal(2, engine.LastProposal.Participants.Count);
        Assert.Contains(engine.LastProposal.Movements, movement => movement.Kind == TradeAssetKind.DraftPick);
    }

    [Fact]
    public void AssessTrade_ResolvesTeamNamesOntoEveryFindingAndOutcome()
    {
        var session = NewSession(out var engine, out var league);
        Assert.True(session.Load().IsSuccess);

        engine.NextViolation = new RuleFinding("trade.salary_not_matched", "Too much salary coming back.", new TeamId(league.FirstTeamId));

        var result = session.AssessTrade(SimpleRequest(league));

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.IsLegal);
        Assert.Equal("Harbourline Tidewatch", Assert.Single(result.Value.Violations).TeamName);
        Assert.Equal("Harbourline Tidewatch", result.Value.Teams[0].TeamName);
    }

    [Fact]
    public void AssessTrade_ExplainsAnIdentifierTheLeagueHasMovedOnFrom()
    {
        var session = NewSession(out _, out var league);
        Assert.True(session.Load().IsSuccess);

        var request = new TradeRequest(
            [league.FirstTeamId, league.SecondTeamId],
            [new TradeAssetRequest(TradeAssetRequest.PlayerKind, "PLAYER-WHO-LEFT", league.FirstTeamId, league.SecondTeamId)]);

        var result = session.AssessTrade(request);

        Assert.True(result.IsFailure);
        Assert.Equal("trade_request.unknown_asset", Assert.Single(result.Errors).Code);
    }

    [Fact]
    public void AssessTrade_RejectsAnAssetKindThisBuildDoesNotKnow()
    {
        var session = NewSession(out _, out var league);
        Assert.True(session.Load().IsSuccess);

        var request = new TradeRequest(
            [league.FirstTeamId, league.SecondTeamId],
            [new TradeAssetRequest("cash", "50000", league.FirstTeamId, league.SecondTeamId)]);

        var result = session.AssessTrade(request);

        // Cash considerations are deferred, and the refusal says so rather than silently ignoring it.
        Assert.True(result.IsFailure);
        Assert.Equal("trade_request.unknown_asset_kind", Assert.Single(result.Errors).Code);
    }

    [Fact]
    public void SubmitTrade_ReturnsTheReprojectedLeagueSoEveryScreenReadsTheNewState()
    {
        var session = NewSession(out var engine, out var league);
        Assert.True(session.Load().IsSuccess);

        engine.LedgerEntryCount = 4;

        var result = session.SubmitTrade(SimpleRequest(league));

        Assert.True(result.IsSuccess, string.Join("; ", result.Errors.Select(error => error.Message)));
        Assert.Equal(4, result.Value.LedgerEntryCount);
        Assert.NotNull(result.Value.Overview);
        Assert.Equal(2, result.Value.Overview.Teams.Count);
    }

    [Fact]
    public void SubmitTrade_PassesTheEngineFailureStraightThroughToTheCaller()
    {
        var session = NewSession(out var engine, out var league);
        Assert.True(session.Load().IsSuccess);

        engine.ExecutionFailure = new DomainError("trade.stale_proposal", "The league has changed.");

        var result = session.SubmitTrade(SimpleRequest(league));

        Assert.True(result.IsFailure);
        Assert.Equal("trade.stale_proposal", Assert.Single(result.Errors).Code);
    }

    private static TradeRequest SimpleRequest(TestLeagueData league) =>
        new(
            [league.FirstTeamId, league.SecondTeamId],
            [new TradeAssetRequest(TradeAssetRequest.PlayerKind, league.FirstTeamPlayerId, league.FirstTeamId, league.SecondTeamId)]);

    private static LeagueSession NewSession(out StubTradeEngine engine, out TestLeagueData league)
    {
        var data = new TestLeagueData();
        league = data;
        engine = new StubTradeEngine(data);

        return new LeagueSession(data.DataSource, new PassThroughCapLedger(), new EmptyDraftAssetLedger(), engine, new StubSigningEngine());
    }

    /// <summary>Two teams, one player each, one pick each — the smallest league a trade can happen in.</summary>
    private sealed class TestLeagueData
    {
        private static readonly Season CurrentSeason = new(2031);

        public TestLeagueData()
        {
            var leagueId = new LeagueId("LEAGUE-TEST");
            var firstFranchise = Franchise.Create(new FranchiseId("FRANCHISE-1"), "Harbourline Sporting Club").Value;
            var secondFranchise = Franchise.Create(new FranchiseId("FRANCHISE-2"), "Verdanmoor Basketball Club").Value;

            var firstPlayer = Player.Create(
                new PlayerId("PLAYER-1"),
                "First Player",
                Position.PointGuard,
                new PlayerRating(70),
                new DateOnly(2001, 2, 3),
                seasonsOfService: 6).Value;

            var secondPlayer = Player.Create(
                new PlayerId("PLAYER-2"),
                "Second Player",
                Position.Center,
                new PlayerRating(72),
                new DateOnly(2004, 9, 14),
                seasonsOfService: 2).Value;

            var limits = new RosterSizeLimits(1, 5);
            var firstTeam = Team.Create(new TeamId("TEAM-1"), firstFranchise.Id, "Harbourline Tidewatch", limits, [firstPlayer.Id]).Value;
            var secondTeam = Team.Create(new TeamId("TEAM-2"), secondFranchise.Id, "Verdanmoor Kestrels", limits, [secondPlayer.Id]).Value;

            var book = new DraftAssetBook(leagueId);
            book.Register(DraftPick.Create(new DraftPickId("PICK-1"), leagueId, new Season(2032), 1, firstFranchise.Id).Value);
            book.Register(DraftPick.Create(new DraftPickId("PICK-2"), leagueId, new Season(2032), 1, secondFranchise.Id).Value);

            Snapshot = new LeagueSnapshot(
                League.Create(leagueId, "Continental Basketball Association", [firstTeam.Id, secondTeam.Id]).Value,
                CurrentSeason,
                [firstFranchise, secondFranchise],
                [firstTeam, secondTeam],
                [firstPlayer, secondPlayer],
                [],
                book,
                new Domain.Transactions.TransactionLedger(new FixedTestClock(new DateTimeOffset(2031, 7, 1, 9, 0, 0, TimeSpan.Zero))),
                new LeagueConfiguration(
                    "Test Ruleset",
                    78,
                    limits,
                    new Money(127_000_000),
                    new Money(141_000_000),
                    new Money(172_000_000),
                    new Money(179_000_000),
                    new Money(190_000_000),
                    new Money(205_000_000),
                    DraftRoundCount: 1,
                    DraftLotteryEnabled: true,
                    TradableFutureDraftHorizon: 1,
                    RetainedRoundNumber: 1,
                    RetainedRoundInterval: 1,
                    SalaryMatchPercent: 125,
                    SalaryMatchAllowance: new Money(250_000),
                    InjuredPlayerTradeEligibility: InjuredPlayerTradeEligibility.AllowedWithWarning,
                    SecondApronBlocksSalaryIncrease: true,
                    Negotiation: NegotiationConfiguration.OpenMarket));

            DataSource = new StubLeagueDataSource(DomainOperationResult<LeagueSnapshot>.Success(Snapshot));
        }

        public LeagueSnapshot Snapshot { get; }

        public ILeagueDataSource DataSource { get; }

        public string FirstTeamId => "TEAM-1";

        public string SecondTeamId => "TEAM-2";

        public string FirstTeamPlayerId => "PLAYER-1";

        public string SecondTeamPickId => "PICK-2";
    }

    private sealed class StubTradeEngine(TestLeagueData league) : ITradeEngine
    {
        public TradeProposal? LastProposal { get; private set; }

        public RuleFinding? NextViolation { get; set; }

        public int LedgerEntryCount { get; set; }

        public DomainError? ExecutionFailure { get; set; }

        public DomainOperationResult<TradeAssessment> Assess(TradeProposal proposal, LeagueSnapshot snapshot)
        {
            LastProposal = proposal;
            return DomainOperationResult<TradeAssessment>.Success(BuildAssessment(proposal));
        }

        public DomainOperationResult<TradeResult> Execute(TradeProposal proposal, LeagueSnapshot snapshot)
        {
            LastProposal = proposal;

            return ExecutionFailure is not null
                ? DomainOperationResult<TradeResult>.Failure(ExecutionFailure)
                : DomainOperationResult<TradeResult>.Success(new TradeResult(BuildAssessment(proposal), LedgerEntryCount));
        }

        private TradeAssessment BuildAssessment(TradeProposal proposal) =>
            new(
                proposal.Id,
                NextViolation is null ? [] : [NextViolation],
                [],
                [],
                proposal.Participants
                    .Select(teamId => new TradeTeamOutcome(
                        teamId,
                        Money.Zero,
                        Money.Zero,
                        Money.Zero,
                        Money.Zero,
                        1,
                        1,
                        1,
                        1,
                        []))
                    .ToList());
    }

    private sealed class PassThroughCapLedger : ICapLedger
    {
        public DomainOperationResult<TeamCapSheet> Evaluate(
            TeamId teamId,
            Season season,
            IReadOnlyCollection<CapCharge> charges,
            int filledRosterSpots,
            LeagueConfiguration configuration) =>
            DomainOperationResult<TeamCapSheet>.Success(new TeamCapSheet(
                teamId, season, Money.Zero, Money.Zero, Money.Zero, Money.Zero, [], []));
    }

    private sealed class EmptyDraftAssetLedger : IDraftAssetLedger
    {
        public DomainOperationResult<DraftAssetBoard> BuildBoard(
            DraftAssetBook book,
            IReadOnlyList<FranchiseDraftIdentity> franchises,
            Season firstDraftSeason,
            LeagueConfiguration configuration) =>
            DomainOperationResult<DraftAssetBoard>.Success(new DraftAssetBoard(
                firstDraftSeason,
                1,
                1,
                franchises.Select(franchise => new DraftAssetBoardRow(franchise.FranchiseId, [])).ToList()));

        public DomainOperationResult ValidateTransfer(
            DraftAssetBook book,
            DraftPickId pickId,
            FranchiseId fromFranchiseId,
            FranchiseId toFranchiseId,
            Season currentSeason,
            LeagueConfiguration configuration) =>
            DomainOperationResult.Success;
    }

    private sealed class StubLeagueDataSource(DomainOperationResult<LeagueSnapshot> result) : ILeagueDataSource
    {
        public DomainOperationResult<LeagueSnapshot> Load() => result;
    }

    private sealed class FixedTestClock(DateTimeOffset instant) : IClock
    {
        public DateTimeOffset UtcNow => instant;
    }
}
