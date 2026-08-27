using BallGM.Domain.Common;

namespace BallGM.Domain.Tests;

/// <summary>
/// The tier-table primitive. Three ruleset tables key off it — compensation floors and ceilings now,
/// the draft-slot scale and the tax brackets later — so the lookup rule and the refusals are worth
/// pinning here rather than three times over in the tables that use them.
/// </summary>
public sealed class BandedScaleTests
{
    [Fact]
    public void ValueForReturnsTheHighestBandTheKeyReaches()
    {
        var scale = BandedScale.Create([new ScaleBand(0, 100), new ScaleBand(3, 200), new ScaleBand(10, 300)]).Value;

        Assert.Equal(100, scale.ValueFor(0));
        Assert.Equal(100, scale.ValueFor(2));
        Assert.Equal(200, scale.ValueFor(3));
        Assert.Equal(200, scale.ValueFor(9));
        Assert.Equal(300, scale.ValueFor(10));
        Assert.Equal(300, scale.ValueFor(25));
    }

    [Fact]
    public void BandsOutOfOrderInTheFileAreStillReadInOrder()
    {
        var scale = BandedScale.Create([new ScaleBand(10, 300), new ScaleBand(0, 100), new ScaleBand(3, 200)]).Value;

        Assert.Equal([0L, 3L, 10L], scale.Bands.Select(band => band.MinimumKey));
        Assert.Equal(200, scale.ValueFor(4));
    }

    /// <summary>
    /// An unconfigured table is a table the league does not have. It answers <c>null</c> rather than
    /// nought, because "no minimum salary" and "a minimum salary of nothing" are different rules and
    /// only one of them is worth telling a GM about.
    /// </summary>
    [Fact]
    public void AnEmptyScaleIsAbsentRatherThanAScaleOfZeroes()
    {
        var scale = BandedScale.Create([]).Value;

        Assert.True(scale.IsEmpty);
        Assert.Null(scale.ValueFor(0));
        Assert.Null(scale.ValueFor(12));
        Assert.Same(BandedScale.None, BandedScale.Create(null).Value);
    }

    /// <summary>
    /// Without a band covering the bottom of the range there is a key with no answer, and the only
    /// alternatives are inventing one or throwing at lookup time. The file is refused instead.
    /// </summary>
    [Fact]
    public void AScaleThatDoesNotStartAtZeroIsRefusedRatherThanGuessedAt()
    {
        var result = BandedScale.Create([new ScaleBand(3, 200), new ScaleBand(10, 300)]);

        Assert.True(result.IsFailure);
        Assert.Equal("scale.missing_base_band", Assert.Single(result.Errors).Code);
    }

    [Fact]
    public void TwoBandsStartingAtTheSameKeyAreRefused()
    {
        var result = BandedScale.Create([new ScaleBand(0, 100), new ScaleBand(3, 200), new ScaleBand(3, 250)]);

        Assert.True(result.IsFailure);
        Assert.Equal("scale.duplicate_band_key", Assert.Single(result.Errors).Code);
    }

    [Fact]
    public void ANegativeBandValueIsRefused()
    {
        var result = BandedScale.Create([new ScaleBand(0, -1)]);

        Assert.True(result.IsFailure);
        Assert.Equal("scale.negative_band_value", Assert.Single(result.Errors).Code);
    }
}
