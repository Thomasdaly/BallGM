using BallGM.Domain.Common;
using BallGM.Domain.DraftAssets;
using BallGM.Domain.Leagues;
using BallGM.Domain.Players;
using BallGM.Domain.Teams;
using BallGM.Domain.Trades;
using BallGM.Domain.Transactions;

namespace BallGM.Domain.Tests;

public sealed class TradeProposalTests
{
    private static readonly Season Season2031 = new(2031);
    private static readonly DateTimeOffset Instant = new(2031, 7, 1, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Proposal_AcceptsATwoTeamExchangeOfAPlayerForAPick()
    {
        var first = NewTeam();
        var second = NewTeam();
        var player = NewPlayer();
        var pick = NewPick();

        var result = TradeProposal.Create(
            NewTradeId(),
            Season2031,
            [first, second],
            [
                TradeAssetMovement.Player(player, first, second),
                TradeAssetMovement.DraftPick(pick, second, first),
            ],
            new LeagueStateToken(0));

        Assert.True(result.IsSuccess, string.Join("; ", result.Errors.Select(error => error.Message)));
        Assert.Single(result.Value.SentBy(first));
        Assert.Single(result.Value.ReceivedBy(first));
    }

    [Fact]
    public void Proposal_AcceptsAThreeTeamTradeBecauseEveryMovementNamesBothEnds()
    {
        var first = NewTeam();
        var second = NewTeam();
        var third = NewTeam();

        var result = TradeProposal.Create(
            NewTradeId(),
            Season2031,
            [first, second, third],
            [
                TradeAssetMovement.Player(NewPlayer(), first, second),
                TradeAssetMovement.Player(NewPlayer(), second, third),
                TradeAssetMovement.DraftPick(NewPick(), third, first),
            ],
            new LeagueStateToken(0));

        Assert.True(result.IsSuccess, string.Join("; ", result.Errors.Select(error => error.Message)));
        Assert.Equal(3, result.Value.Participants.Count);
    }

    [Fact]
    public void Proposal_RejectsATeamTradingWithItself()
    {
        var team = NewTeam();

        var result = TradeProposal.Create(
            NewTradeId(),
            Season2031,
            [team],
            [TradeAssetMovement.Player(NewPlayer(), team, team)],
            new LeagueStateToken(0));

        Assert.True(result.IsFailure);
        Assert.Contains(result.Errors, error => error.Code == "trade.too_few_participants");
        Assert.Contains(result.Errors, error => error.Code == "trade.asset_sent_to_itself");
    }

    [Fact]
    public void Proposal_RejectsATradeThatMovesNothing()
    {
        var result = TradeProposal.Create(
            NewTradeId(),
            Season2031,
            [NewTeam(), NewTeam()],
            [],
            new LeagueStateToken(0));

        Assert.True(result.IsFailure);
        Assert.Contains(result.Errors, error => error.Code == "trade.no_assets");
    }

    [Fact]
    public void Proposal_RejectsTheSameAssetBeingSentTwice()
    {
        var first = NewTeam();
        var second = NewTeam();
        var third = NewTeam();
        var player = NewPlayer();

        var result = TradeProposal.Create(
            NewTradeId(),
            Season2031,
            [first, second, third],
            [
                TradeAssetMovement.Player(player, first, second),
                TradeAssetMovement.Player(player, first, third),
            ],
            new LeagueStateToken(0));

        Assert.True(result.IsFailure);
        Assert.Contains(result.Errors, error => error.Code == "trade.asset_moved_twice");
    }

    [Fact]
    public void Proposal_RejectsAnAssetMovingToATeamOutsideTheTrade()
    {
        var first = NewTeam();
        var second = NewTeam();
        var outsider = NewTeam();

        var result = TradeProposal.Create(
            NewTradeId(),
            Season2031,
            [first, second],
            [
                TradeAssetMovement.Player(NewPlayer(), first, second),
                TradeAssetMovement.Player(NewPlayer(), second, outsider),
            ],
            new LeagueStateToken(0));

        Assert.True(result.IsFailure);
        Assert.Contains(result.Errors, error => error.Code == "trade.movement_outside_participants");
    }

    [Fact]
    public void Proposal_RejectsAParticipantWithNothingAtStake()
    {
        var first = NewTeam();
        var second = NewTeam();
        var bystander = NewTeam();

        var result = TradeProposal.Create(
            NewTradeId(),
            Season2031,
            [first, second, bystander],
            [TradeAssetMovement.Player(NewPlayer(), first, second)],
            new LeagueStateToken(0));

        Assert.True(result.IsFailure);
        Assert.Contains(result.Errors, error => error.Code == "trade.participant_sends_and_receives_nothing");
    }

    [Fact]
    public void Proposal_GoesStaleAsSoonAsTheLeagueRecordsAnythingElse()
    {
        var ledger = new TransactionLedger(new FixedTestClock(Instant));
        var first = NewTeam();
        var second = NewTeam();

        var proposal = TradeProposal.Create(
            NewTradeId(),
            Season2031,
            [first, second],
            [TradeAssetMovement.Player(NewPlayer(), first, second)],
            LeagueStateToken.From(ledger)).Value;

        Assert.False(proposal.IsStaleAgainst(ledger));

        ledger.Record(TransactionKind.ContractSigned, Season2031, first, "Someone else signed a deal.");

        // The league moved on. The player in this proposal may already have been traded.
        Assert.True(proposal.IsStaleAgainst(ledger));
    }

    private static TradeId NewTradeId() => new(SortableId.NewId());

    private static TeamId NewTeam() => new(SortableId.NewId());

    private static PlayerId NewPlayer() => new(SortableId.NewId());

    private static DraftPickId NewPick() => new(SortableId.NewId());

    private sealed class FixedTestClock(DateTimeOffset instant) : IClock
    {
        public DateTimeOffset UtcNow => instant;
    }
}
