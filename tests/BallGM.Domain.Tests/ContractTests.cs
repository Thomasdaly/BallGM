using BallGM.Domain.Common;
using BallGM.Domain.Contracts;
using BallGM.Domain.Leagues;
using BallGM.Domain.Players;
using BallGM.Domain.Teams;

namespace BallGM.Domain.Tests;

public sealed class ContractTests
{
    private static readonly Season FirstSeason = new(2031);

    [Fact]
    public void Contract_CarriesCompensationSeasonBySeason()
    {
        var contract = Unwrap(CreateContract(
            Term(2031, 20_000_000),
            Term(2032, 21_000_000),
            Term(2033, 22_000_000)));

        Assert.Equal(2031, contract.FirstSeason.Year);
        Assert.Equal(2033, contract.LastSeason.Year);
        Assert.Equal(21_000_000, contract.TermFor(new Season(2032))!.Compensation.SmallestUnits);
        Assert.Null(contract.TermFor(new Season(2034)));
    }

    [Fact]
    public void Contract_SortsSeasonsSoAFileMayListThemInAnyOrder()
    {
        var contract = Unwrap(CreateContract(
            Term(2033, 22_000_000),
            Term(2031, 20_000_000),
            Term(2032, 21_000_000)));

        Assert.Equal([2031, 2032, 2033], contract.Terms.Select(term => term.Season.Year));
    }

    [Fact]
    public void Contract_RejectsAnEmptySeasonRange()
    {
        var result = Contract.Create(
            new ContractId(SortableId.NewId()),
            new TeamId(SortableId.NewId()),
            new PlayerId(SortableId.NewId()),
            []);

        Assert.True(result.IsFailure);
        Assert.Equal("contract.no_seasons", Assert.Single(result.Errors).Code);
    }

    [Fact]
    public void Contract_RejectsAGapInTheSeasonRange()
    {
        var result = CreateContract(Term(2031, 20_000_000), Term(2033, 20_000_000));

        Assert.True(result.IsFailure);
        Assert.Equal("contract.seasons_not_contiguous", Assert.Single(result.Errors).Code);
    }

    [Fact]
    public void Contract_RejectsTwoTermsForTheSameSeason()
    {
        var result = CreateContract(Term(2031, 20_000_000), Term(2031, 18_000_000));

        Assert.True(result.IsFailure);
        Assert.Equal("contract.duplicate_season", Assert.Single(result.Errors).Code);
    }

    [Fact]
    public void Contract_RejectsGuaranteeingMoreThanTheSeasonPays()
    {
        var result = CreateContract(new ContractSeasonTerm(FirstSeason, new Money(10_000_000), new Money(12_000_000)));

        Assert.True(result.IsFailure);
        Assert.Equal("contract.guarantee_exceeds_compensation", Assert.Single(result.Errors).Code);
    }

    [Fact]
    public void ActiveContract_ChargesTheSeasonsFullCompensation()
    {
        var contract = Unwrap(CreateContract(Term(2031, 20_000_000), Term(2032, 21_000_000)));

        var charge = contract.ChargeFor(new Season(2032));

        Assert.NotNull(charge);
        Assert.Equal(21_000_000, charge.Amount.SmallestUnits);
        Assert.False(charge.IsDeadMoney);
    }

    [Fact]
    public void ReleasedPlayer_LeavesTheGuaranteedMoneyBehindAsDeadMoney()
    {
        var contract = Unwrap(CreateContract(
            Term(2031, 20_000_000),
            new ContractSeasonTerm(new Season(2032), new Money(21_000_000), new Money(7_000_000))));

        Assert.True(contract.Terminate(new Season(2032)).IsSuccess);

        var charge = contract.ChargeFor(new Season(2032));

        Assert.NotNull(charge);
        Assert.True(charge.IsDeadMoney);
        Assert.Equal(7_000_000, charge.Amount.SmallestUnits);
    }

    [Fact]
    public void ReleasedPlayer_LeavesNothingBehindForAnUnguaranteedSeason()
    {
        var contract = Unwrap(CreateContract(
            Term(2031, 20_000_000),
            new ContractSeasonTerm(new Season(2032), new Money(21_000_000), Money.Zero)));

        Assert.True(contract.Terminate(new Season(2032)).IsSuccess);

        Assert.Null(contract.ChargeFor(new Season(2032)));
    }

    [Fact]
    public void ReleasedPlayer_StillCostsTheSeasonsAlreadyPlayedUnderTheLiveContract()
    {
        var contract = Unwrap(CreateContract(Term(2031, 20_000_000), Term(2032, 21_000_000)));

        Assert.True(contract.Terminate(new Season(2032)).IsSuccess);

        var charge = contract.ChargeFor(new Season(2031));

        Assert.NotNull(charge);
        Assert.False(charge.IsDeadMoney);
        Assert.Equal(20_000_000, charge.Amount.SmallestUnits);
    }

    [Fact]
    public void Release_RejectsASeasonTheContractDoesNotCover()
    {
        var contract = Unwrap(CreateContract(Term(2031, 20_000_000)));

        var result = contract.Terminate(new Season(2035));

        Assert.True(result.IsFailure);
        Assert.Equal("contract.season_not_in_contract", Assert.Single(result.Errors).Code);
    }

    [Fact]
    public void Release_RejectsReleasingTheSamePlayerTwice()
    {
        var contract = Unwrap(CreateContract(Term(2031, 20_000_000), Term(2032, 20_000_000)));
        Assert.True(contract.Terminate(new Season(2032)).IsSuccess);

        var result = contract.Terminate(new Season(2031));

        Assert.True(result.IsFailure);
        Assert.Equal("contract.already_terminated", Assert.Single(result.Errors).Code);
    }

    [Fact]
    public void PendingOptionSeason_IsNotACapChargeUntilItIsExercised()
    {
        var contract = Unwrap(CreateContract(
            Term(2031, 20_000_000),
            OptionTerm(2032, 21_000_000, ContractOptionKind.Team)));

        Assert.Null(contract.ChargeFor(new Season(2032)));

        Assert.True(contract.ExerciseOption(new Season(2032)).IsSuccess);

        var charge = contract.ChargeFor(new Season(2032));
        Assert.NotNull(charge);
        Assert.Equal(21_000_000, charge.Amount.SmallestUnits);
    }

    [Fact]
    public void DeclinedOption_EndsTheContractWithoutLeavingDeadMoney()
    {
        var contract = Unwrap(CreateContract(
            Term(2031, 20_000_000),
            OptionTerm(2032, 21_000_000, ContractOptionKind.Player)));

        Assert.True(contract.DeclineOption(new Season(2032)).IsSuccess);

        Assert.Null(contract.ChargeFor(new Season(2032)));
        Assert.NotNull(contract.ChargeFor(new Season(2031)));
    }

    [Fact]
    public void Option_CannotBeDecidedTwice()
    {
        var contract = Unwrap(CreateContract(
            Term(2031, 20_000_000),
            OptionTerm(2032, 21_000_000, ContractOptionKind.Team)));

        Assert.True(contract.ExerciseOption(new Season(2032)).IsSuccess);
        var result = contract.DeclineOption(new Season(2032));

        Assert.True(result.IsFailure);
        Assert.Equal("contract.option_already_decided", Assert.Single(result.Errors).Code);
    }

    [Fact]
    public void Option_CannotBeDecidedOnASeasonThatCarriesNone()
    {
        var contract = Unwrap(CreateContract(Term(2031, 20_000_000), Term(2032, 21_000_000)));

        var result = contract.ExerciseOption(new Season(2032));

        Assert.True(result.IsFailure);
        Assert.Equal("contract.season_has_no_option", Assert.Single(result.Errors).Code);
    }

    [Fact]
    public void Option_CannotBeDecidedAfterThePlayerHasBeenReleased()
    {
        var contract = Unwrap(CreateContract(
            Term(2031, 20_000_000),
            OptionTerm(2032, 21_000_000, ContractOptionKind.Team)));

        Assert.True(contract.Terminate(new Season(2031)).IsSuccess);
        var result = contract.ExerciseOption(new Season(2032));

        Assert.True(result.IsFailure);
        Assert.Equal("contract.already_terminated", Assert.Single(result.Errors).Code);
    }

    [Fact]
    public void SeasonOutsideTheContract_CostsNothing()
    {
        var contract = Unwrap(CreateContract(Term(2031, 20_000_000)));

        Assert.Null(contract.ChargeFor(new Season(2030)));
        Assert.Null(contract.ChargeFor(new Season(2032)));
    }

    private static DomainOperationResult<Contract> CreateContract(params ContractSeasonTerm[] terms) =>
        Contract.Create(
            new ContractId(SortableId.NewId()),
            new TeamId(SortableId.NewId()),
            new PlayerId(SortableId.NewId()),
            terms);

    private static ContractSeasonTerm Term(int year, long compensation) =>
        new(new Season(year), new Money(compensation), new Money(compensation));

    private static ContractSeasonTerm OptionTerm(int year, long compensation, ContractOptionKind option) =>
        new(new Season(year), new Money(compensation), Money.Zero, option);

    private static Contract Unwrap(DomainOperationResult<Contract> result)
    {
        Assert.True(result.IsSuccess, string.Join("; ", result.Errors.Select(error => error.Message)));
        return result.Value;
    }
}
