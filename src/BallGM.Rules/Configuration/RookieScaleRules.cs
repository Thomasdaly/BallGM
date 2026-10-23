using BallGM.Domain.Common;
using BallGM.Domain.Contracts;
using BallGM.Domain.Leagues;

namespace BallGM.Rules.Configuration;

/// <summary>
/// How a drafted player's first contract is written (<c>docs/franchise-realism.md</c> → F3): a
/// first-round pick's salary is a fixed share of the soft cap by where he was taken, for a fixed number
/// of seasons of which the last are the team's options; a second-round pick signs at the league minimum.
/// The generic form of the real agreement's rookie scale: the slot, not a negotiation, sets the pay,
/// and the team decides later whether the option years happen. Absent, a draftee signs a one-season
/// minimum deal (the pre-scale behaviour).
/// </summary>
public sealed record RookieScaleRules
{
    private const string InvalidSeasonsCode = "ruleset.invalid_rookie_scale_seasons";
    private const string InvalidScaleCode = "ruleset.invalid_rookie_scale";

    /// <summary>The most a scale may pay one pick, in basis points of the soft cap: a slot is not a max contract.</summary>
    public const int MaximumShareBasisPoints = 2_500;

    /// <summary>The raise each scale season carries over the one before, in per cent.</summary>
    public const int AnnualRaisePercent = 5;

    private RookieScaleRules(BandedScale firstRoundShare, int seasons, int teamOptionSeasons, int secondRoundSeasons)
    {
        FirstRoundShare = firstRoundShare;
        Seasons = seasons;
        TeamOptionSeasons = teamOptionSeasons;
        SecondRoundSeasons = secondRoundSeasons;
    }

    /// <summary>A league with no rookie scale.</summary>
    public static RookieScaleRules None { get; } = new(BandedScale.None, 0, 0, 0);

    public bool IsConfigured => Seasons > 0;

    /// <summary>A first-round pick's first-season salary by selection number, in basis points of the soft cap.</summary>
    public BandedScale FirstRoundShare { get; }

    /// <summary>How many seasons a first-round scale contract covers.</summary>
    public int Seasons { get; }

    /// <summary>How many of those seasons, at the end, are the team's options.</summary>
    public int TeamOptionSeasons { get; }

    /// <summary>How many seasons a second-round pick's minimum contract covers.</summary>
    public int SecondRoundSeasons { get; }

    public static DomainOperationResult<RookieScaleRules> Create(BandedScale firstRoundShare, int seasons, int teamOptionSeasons, int secondRoundSeasons)
    {
        ArgumentNullException.ThrowIfNull(firstRoundShare);
        if (seasons is < 1 or > 6 || teamOptionSeasons < 0 || teamOptionSeasons >= seasons || secondRoundSeasons is < 1 or > 4)
        {
            return DomainOperationResult<RookieScaleRules>.Failure(new DomainError(InvalidSeasonsCode,
                $"A rookie scale of {seasons} seasons ({teamOptionSeasons} of them options) and {secondRoundSeasons} for a second-round pick is not a rule: 1 to 6 seasons, fewer options than seasons, 1 to 4 for the second round."));
        }

        if (firstRoundShare.Bands.Count == 0 || firstRoundShare.Bands.Any(band => band.Value is < 1 or > MaximumShareBasisPoints))
        {
            return DomainOperationResult<RookieScaleRules>.Failure(new DomainError(InvalidScaleCode,
                $"A rookie scale states a share of the cap for every first-round pick, each 1 to {MaximumShareBasisPoints} basis points."));
        }

        return DomainOperationResult<RookieScaleRules>.Success(new RookieScaleRules(firstRoundShare, seasons, teamOptionSeasons, secondRoundSeasons));
    }

    /// <summary>
    /// The contract a pick signs: for the first round, the scale salary from <paramref name="firstSeason"/>
    /// rising <see cref="AnnualRaisePercent"/> a season, the last <see cref="TeamOptionSeasons"/> as team
    /// options; for the second round, the minimum for <see cref="SecondRoundSeasons"/> seasons. Never
    /// below the minimum. Null in a league with no scale or no cap to take a share of.
    /// </summary>
    public IReadOnlyList<ContractSeasonTerm>? TermsFor(int round, int selection, Season firstSeason, Money? softCap, Money minimum)
    {
        ArgumentNullException.ThrowIfNull(firstSeason);
        ArgumentNullException.ThrowIfNull(minimum);
        if (!IsConfigured)
        {
            return null;
        }

        if (round > 1)
        {
            return Enumerable.Range(0, SecondRoundSeasons)
                .Select(offset => new ContractSeasonTerm(new Season(firstSeason.Year + offset), minimum, minimum))
                .ToList();
        }

        if (softCap is null)
        {
            return null;
        }

        var share = FirstRoundShare.ValueFor(selection) ?? 0;
        var salary = Math.Max(minimum.SmallestUnits, softCap.SmallestUnits * share / 10_000 / 10_000 * 10_000);
        return Enumerable.Range(0, Seasons)
            .Select(offset =>
            {
                var pay = new Money(salary + (salary * AnnualRaisePercent * offset / 100));
                return offset >= Seasons - TeamOptionSeasons
                    ? new ContractSeasonTerm(new Season(firstSeason.Year + offset), pay, pay, ContractOptionKind.Team)
                    : new ContractSeasonTerm(new Season(firstSeason.Year + offset), pay, pay);
            })
            .ToList();
    }
}
