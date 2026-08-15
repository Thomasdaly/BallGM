using BallGM.Domain.Common;
using BallGM.Domain.Contracts;
using BallGM.Domain.Leagues;
using BallGM.Domain.Players;
using BallGM.Domain.Teams;
using BallGM.Infrastructure.Contracts;

namespace BallGM.Integration.Tests;

/// <summary>
/// Contracts are save and data-pack surface, so they round-trip through a versioned envelope and
/// fail explainably — never by throwing — on content this build cannot read.
/// </summary>
public sealed class ContractSerializerTests
{
    private static readonly ContractSerializer Serializer = new();

    [Fact]
    public void Contract_SurvivesASaveAndLoadRoundTripIntact()
    {
        var original = Unwrap(Contract.Create(
            new ContractId("CONTRACT-1"),
            new TeamId("TEAM-1"),
            new PlayerId("PLAYER-1"),
            [
                new ContractSeasonTerm(new Season(2031), new Money(20_000_000), new Money(20_000_000)),
                new ContractSeasonTerm(new Season(2032), new Money(21_000_000), new Money(10_500_000)),
                new ContractSeasonTerm(new Season(2033), new Money(22_000_000), Money.Zero, ContractOptionKind.Player),
            ]));

        var restored = Unwrap(Serializer.Deserialize(Serializer.Serialize(original)));

        Assert.Equal(original.Id, restored.Id);
        Assert.Equal(original.TeamId, restored.TeamId);
        Assert.Equal(original.PlayerId, restored.PlayerId);
        Assert.Equal(original.Terms, restored.Terms);
        Assert.Equal(
            original.ChargeFor(new Season(2031))!.Amount,
            restored.ChargeFor(new Season(2031))!.Amount);
    }

    [Fact]
    public void ReleasedContract_KeepsItsDeadMoneyAcrossARoundTrip()
    {
        var original = Unwrap(Contract.Create(
            new ContractId("CONTRACT-2"),
            new TeamId("TEAM-1"),
            new PlayerId("PLAYER-2"),
            [
                new ContractSeasonTerm(new Season(2031), new Money(14_400_000), new Money(7_200_000)),
                new ContractSeasonTerm(new Season(2032), new Money(14_400_000), Money.Zero),
            ]));

        Assert.True(original.Terminate(new Season(2031)).IsSuccess);

        var restored = Unwrap(Serializer.Deserialize(Serializer.Serialize(original)));

        Assert.True(restored.IsTerminated);
        Assert.Equal(2031, restored.TerminatedFromSeason!.Year);

        var charge = restored.ChargeFor(new Season(2031));
        Assert.NotNull(charge);
        Assert.True(charge.IsDeadMoney);
        Assert.Equal(7_200_000, charge.Amount.SmallestUnits);
    }

    [Fact]
    public void DecidedOptions_SurviveARoundTrip()
    {
        var original = Unwrap(Contract.Create(
            new ContractId("CONTRACT-3"),
            new TeamId("TEAM-1"),
            new PlayerId("PLAYER-3"),
            [
                new ContractSeasonTerm(new Season(2031), new Money(9_000_000), new Money(9_000_000)),
                new ContractSeasonTerm(new Season(2032), new Money(9_500_000), Money.Zero, ContractOptionKind.Team),
            ]));

        Assert.True(original.ExerciseOption(new Season(2032)).IsSuccess);

        var restored = Unwrap(Serializer.Deserialize(Serializer.Serialize(original)));

        Assert.Equal(ContractOptionStatus.Exercised, restored.Terms[^1].OptionStatus);
        Assert.Equal(9_500_000, restored.ChargeFor(new Season(2032))!.Amount.SmallestUnits);
    }

    [Fact]
    public void SerializedContract_DeclaresItsSchemaVersion()
    {
        var contract = Unwrap(Contract.Create(
            new ContractId("CONTRACT-4"),
            new TeamId("TEAM-1"),
            new PlayerId("PLAYER-4"),
            [new ContractSeasonTerm(new Season(2031), new Money(1_000_000), new Money(1_000_000))]));

        Assert.Contains("\"schemaVersion\": 1", Serializer.Serialize(contract), StringComparison.Ordinal);
    }

    [Fact]
    public void ContractFromAFutureSchema_ExplainsItselfInsteadOfLoadingHalfOfIt()
    {
        var result = Serializer.Deserialize(
            """
            {
              "schemaVersion": 99,
              "contractId": "CONTRACT-5",
              "teamId": "TEAM-1",
              "playerId": "PLAYER-5",
              "seasons": []
            }
            """);

        Assert.True(result.IsFailure);
        Assert.Equal("contract.unsupported_schema_version", Assert.Single(result.Errors).Code);
    }

    [Fact]
    public void MalformedContractJson_ExplainsItselfInsteadOfThrowing()
    {
        var result = Serializer.Deserialize("{ this is not json");

        Assert.True(result.IsFailure);
        Assert.Equal("contract.malformed_file", Assert.Single(result.Errors).Code);
    }

    [Fact]
    public void ContractWithAnUnknownOption_ExplainsItselfInsteadOfThrowing()
    {
        var result = Serializer.Deserialize(
            """
            {
              "schemaVersion": 1,
              "contractId": "CONTRACT-6",
              "teamId": "TEAM-1",
              "playerId": "PLAYER-6",
              "seasons": [
                {
                  "seasonYear": 2031,
                  "compensation": 1000000,
                  "guaranteedAmount": 1000000,
                  "option": "CoachOption",
                  "optionStatus": "NotApplicable"
                }
              ]
            }
            """);

        Assert.True(result.IsFailure);
        Assert.Equal("contract.invalid_field", Assert.Single(result.Errors).Code);
    }

    [Fact]
    public void ContractWithNegativeMoney_ExplainsItselfInsteadOfThrowing()
    {
        var result = Serializer.Deserialize(
            """
            {
              "schemaVersion": 1,
              "contractId": "CONTRACT-7",
              "teamId": "TEAM-1",
              "playerId": "PLAYER-7",
              "seasons": [
                {
                  "seasonYear": 2031,
                  "compensation": -5,
                  "guaranteedAmount": 0,
                  "option": "None",
                  "optionStatus": "NotApplicable"
                }
              ]
            }
            """);

        Assert.True(result.IsFailure);
        Assert.Equal("contract.invalid_field", Assert.Single(result.Errors).Code);
    }

    [Fact]
    public void ContractGuaranteeingMoreThanItPays_FailsTheSameWayItWouldInMemory()
    {
        var result = Serializer.Deserialize(
            """
            {
              "schemaVersion": 1,
              "contractId": "CONTRACT-8",
              "teamId": "TEAM-1",
              "playerId": "PLAYER-8",
              "seasons": [
                {
                  "seasonYear": 2031,
                  "compensation": 1000000,
                  "guaranteedAmount": 2000000,
                  "option": "None",
                  "optionStatus": "NotApplicable"
                }
              ]
            }
            """);

        Assert.True(result.IsFailure);
        Assert.Equal("contract.guarantee_exceeds_compensation", Assert.Single(result.Errors).Code);
    }

    private static Contract Unwrap(DomainOperationResult<Contract> result)
    {
        Assert.True(result.IsSuccess, string.Join("; ", result.Errors.Select(error => error.Message)));
        return result.Value;
    }
}
