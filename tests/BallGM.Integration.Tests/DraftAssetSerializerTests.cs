using BallGM.Domain.Common;
using BallGM.Domain.DraftAssets;
using BallGM.Domain.Franchises;
using BallGM.Domain.Leagues;
using BallGM.Infrastructure.DraftAssets;

namespace BallGM.Integration.Tests;

public sealed class DraftAssetSerializerTests
{
    private static readonly LeagueId League = new("LEAGUE-CONTINENTAL");
    private static readonly FranchiseId Original = new("FRANCHISE-HARBOURLINE");
    private static readonly FranchiseId Beneficiary = new("FRANCHISE-VERDANMOOR");
    private static readonly FranchiseId Holder = new("FRANCHISE-NORTHREACH");

    [Fact]
    public void RoundTrip_PreservesAnEncumberedPickThatIsHalfWayThroughItsRollover()
    {
        var serializer = new DraftAssetSerializer();
        var book = new DraftAssetBook(League);
        var pick = Register(book, 2033, 1, Original);
        Register(book, 2034, 1, Original);

        // Traded once, then promised on with a protection that has already used up one of its years.
        Assert.True(book.Transfer(pick.Id, Holder).IsSuccess);
        var protection = PickProtection.TopSelections([10, 8, 6], PickProtectionFallback.ConvertsToLaterRound(2).Value).Value;
        Assert.True(book.Encumber(
            pick.Id,
            new PickObligation(new PickEncumbranceId("ENCUMBRANCE-ROLLED"), Beneficiary, protection, scheduleIndex: 1)).IsSuccess);

        var result = serializer.Deserialize(serializer.Serialize(book));

        Assert.True(result.IsSuccess, string.Join("; ", result.Errors.Select(error => error.Message)));

        var reloaded = result.Value.Ownership(pick.Id)!;
        Assert.Equal(Original, result.Value.Pick(pick.Id)!.OriginalFranchiseId);
        Assert.Equal(Holder, reloaded.CurrentOwnerFranchiseId);

        var obligation = reloaded.Obligation!;
        Assert.Equal(1, obligation.ScheduleIndex);
        Assert.Equal(8, obligation.CurrentProtectionLevel);
        Assert.Equal(Beneficiary, obligation.BeneficiaryFranchiseId);
        Assert.Equal(PickProtectionFallbackKind.ConvertsToRound, obligation.Protection.Fallback.Kind);
        Assert.Equal(2, obligation.Protection.Fallback.ConvertsToRound);
    }

    [Fact]
    public void RoundTrip_PreservesASwapRightAndTheAssetItIsHeldAgainst()
    {
        var serializer = new DraftAssetSerializer();
        var book = new DraftAssetBook(League);
        var pick = Register(book, 2033, 1, Original);
        var counterpart = Register(book, 2033, 1, Holder);
        Assert.True(book.Encumber(
            pick.Id,
            new SwapRight(new PickEncumbranceId("ENCUMBRANCE-SWAP"), Holder, counterpart.Id)).IsSuccess);

        var result = serializer.Deserialize(serializer.Serialize(book));

        Assert.True(result.IsSuccess);
        var swap = result.Value.Ownership(pick.Id)!.PendingSwap!;
        Assert.Equal(Holder, swap.HolderFranchiseId);
        Assert.Equal(counterpart.Id, swap.CounterpartPickId);
    }

    [Fact]
    public void Deserialize_RejectsASchemaVersionThisBuildCannotRead()
    {
        var serializer = new DraftAssetSerializer();
        var json = """
            {
              "schemaVersion": 99,
              "leagueId": "LEAGUE-CONTINENTAL",
              "picks": []
            }
            """;

        var result = serializer.Deserialize(json);

        Assert.True(result.IsFailure);
        Assert.Equal("draft_assets.unsupported_schema_version", Assert.Single(result.Errors).Code);
    }

    [Fact]
    public void Deserialize_ReturnsAStructuredFailureForMalformedJsonInsteadOfThrowing()
    {
        var result = new DraftAssetSerializer().Deserialize("{ not json at all");

        Assert.True(result.IsFailure);
        Assert.Equal("draft_assets.malformed_file", Assert.Single(result.Errors).Code);
    }

    [Fact]
    public void Deserialize_RejectsAFileClaimingTwoPicksAreTheSameFranchisesFirstRounder()
    {
        var json = """
            {
              "schemaVersion": 1,
              "leagueId": "LEAGUE-CONTINENTAL",
              "picks": [
                {
                  "pickId": "PICK-A",
                  "draftSeasonYear": 2033,
                  "round": 1,
                  "originalFranchiseId": "FRANCHISE-HARBOURLINE",
                  "currentOwnerFranchiseId": "FRANCHISE-HARBOURLINE"
                },
                {
                  "pickId": "PICK-B",
                  "draftSeasonYear": 2033,
                  "round": 1,
                  "originalFranchiseId": "FRANCHISE-HARBOURLINE",
                  "currentOwnerFranchiseId": "FRANCHISE-VERDANMOOR"
                }
              ]
            }
            """;

        var result = new DraftAssetSerializer().Deserialize(json);

        Assert.True(result.IsFailure);
        Assert.Equal("draft_assets.duplicate_pick_coordinates", Assert.Single(result.Errors).Code);
    }

    [Fact]
    public void Deserialize_RejectsASwapRightPointingAtAPickTheFileDoesNotContain()
    {
        var json = """
            {
              "schemaVersion": 1,
              "leagueId": "LEAGUE-CONTINENTAL",
              "picks": [
                {
                  "pickId": "PICK-A",
                  "draftSeasonYear": 2033,
                  "round": 1,
                  "originalFranchiseId": "FRANCHISE-HARBOURLINE",
                  "currentOwnerFranchiseId": "FRANCHISE-HARBOURLINE",
                  "swapRight": {
                    "encumbranceId": "ENCUMBRANCE-SWAP",
                    "holderFranchiseId": "FRANCHISE-NORTHREACH",
                    "counterpartPickId": "PICK-MISSING"
                  }
                }
              ]
            }
            """;

        var result = new DraftAssetSerializer().Deserialize(json);

        Assert.True(result.IsFailure);
        Assert.Equal("draft_assets.invalid_field", Assert.Single(result.Errors).Code);
    }

    [Fact]
    public void Deserialize_RejectsAnObligationRolledPastTheEndOfItsOwnSchedule()
    {
        var json = """
            {
              "schemaVersion": 1,
              "leagueId": "LEAGUE-CONTINENTAL",
              "picks": [
                {
                  "pickId": "PICK-A",
                  "draftSeasonYear": 2033,
                  "round": 1,
                  "originalFranchiseId": "FRANCHISE-HARBOURLINE",
                  "currentOwnerFranchiseId": "FRANCHISE-HARBOURLINE",
                  "obligation": {
                    "encumbranceId": "ENCUMBRANCE-A",
                    "beneficiaryFranchiseId": "FRANCHISE-VERDANMOOR",
                    "protectedSelections": [4],
                    "fallbackKind": "Extinguishes",
                    "convertsToRound": null,
                    "scheduleIndex": 4
                  }
                }
              ]
            }
            """;

        var result = new DraftAssetSerializer().Deserialize(json);

        Assert.True(result.IsFailure);
        Assert.Equal("draft_assets.invalid_field", Assert.Single(result.Errors).Code);
    }

    [Fact]
    public void Deserialize_RejectsAConvertingFallbackWithNoRoundNamed()
    {
        var json = """
            {
              "schemaVersion": 1,
              "leagueId": "LEAGUE-CONTINENTAL",
              "picks": [
                {
                  "pickId": "PICK-A",
                  "draftSeasonYear": 2033,
                  "round": 1,
                  "originalFranchiseId": "FRANCHISE-HARBOURLINE",
                  "currentOwnerFranchiseId": "FRANCHISE-HARBOURLINE",
                  "obligation": {
                    "encumbranceId": "ENCUMBRANCE-A",
                    "beneficiaryFranchiseId": "FRANCHISE-VERDANMOOR",
                    "protectedSelections": [4],
                    "fallbackKind": "ConvertsToRound",
                    "convertsToRound": null,
                    "scheduleIndex": 0
                  }
                }
              ]
            }
            """;

        var result = new DraftAssetSerializer().Deserialize(json);

        Assert.True(result.IsFailure);
        Assert.Equal("pick_protection.fallback_round_missing", Assert.Single(result.Errors).Code);
    }

    private static DraftPick Register(DraftAssetBook book, int year, int round, FranchiseId original)
    {
        var pick = DraftPick.Create(
            new DraftPickId($"PICK-{original.Value}-{year}-R{round}"),
            League,
            new Season(year),
            round,
            original).Value;

        Assert.True(book.Register(pick).IsSuccess);
        return pick;
    }
}
