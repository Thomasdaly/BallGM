using BallGM.Domain.Common;
using BallGM.Domain.Draft;
using BallGM.Domain.Leagues;
using BallGM.Domain.Players;
using BallGM.Domain.Randomness;
using BallGM.Rules.Configuration;
using BallGM.Rules.Players;

namespace BallGM.Rules.Draft;

/// <summary>
/// Builds one season's <see cref="DraftClass"/> from <see cref="DraftClassRules"/> rather than from
/// hardcoded content — the size of the class, the spread of true rating it draws from, and the youngest
/// age a prospect enters at (each is drawn up to four years older) are all ruleset data, the same pattern <c>ScheduleGenerator</c> already
/// follows for the regular season. Deterministic: the same rules and the same <see cref="IRandomSource"/>
/// state produce the same class, on every platform and every run.
/// <para>
/// A prospect's talent is the average of two draws inside <see cref="DraftClassRules.MinimumRating"/>
/// and <see cref="DraftClassRules.MaximumRating"/> rather than one uniform draw, so most classes cluster
/// mid-range and a prospect at the very top or bottom of the stated spread stays rare — a shape a
/// single uniform draw does not have. <see cref="RatingProfileGenerator"/> turns that one number into
/// a real five-attribute <see cref="PlayerRating"/>, clamped to the same stated band, rather than the
/// class fielding ninety-odd prospects who are all just one number wearing a name. Position is
/// assigned round-robin across the five positions rather than drawn, so a class of any size still
/// fields a roughly even spread rather than risking an all-guard class on an unlucky seed; the
/// position generation itself is exactly as configurable as that decision needs to be, which is not
/// at all, until a league wants to weight it.
/// </para>
/// </summary>
public static class ProspectGenerator
{
    private const string NotConfiguredCode = "draft_class.generator_not_configured";

    public static DomainOperationResult<DraftClass> Generate(
        DraftClassId id,
        Season draftSeason,
        DraftClassRules rules,
        IRandomSource random)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(draftSeason);
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(random);

        if (!rules.IsConfigured)
        {
            return DomainOperationResult<DraftClass>.Failure(new DomainError(
                NotConfiguredCode,
                "This league's ruleset does not configure draft class generation, so no class can be generated."));
        }

        var prospects = new List<Prospect>(rules.ClassSize);
        var positions = Enum.GetValues<Position>();

        for (var index = 0; index < rules.ClassSize; index++)
        {
            var position = positions[index % positions.Length];
            var offset = AgeOffset(random);
            var talent = Math.Min(rules.MaximumRating, GenerateTalent(rules, random) + (offset * PolishPerYearOlder));
            var age = rules.ProspectAgeYears + offset;
            var birthDate = new DateOnly(draftSeason.Year - age, 7, 1);
            var rating = RatingProfileGenerator.Generate(talent, position, age, rules.MinimumRating, rules.MaximumRating, random);
            var name = ProspectNameBank.NextName(random);

            var prospectResult = Prospect.Create(
                new ProspectId(SortableId.NewId()),
                name,
                position,
                birthDate,
                rating);

            if (prospectResult.IsFailure)
            {
                return DomainOperationResult<DraftClass>.Failure(prospectResult.Errors.ToArray());
            }

            prospects.Add(prospectResult.Value);
        }

        return DraftClass.Create(id, draftSeason, prospects);
    }

    /// <summary>
    /// Years older than the youngest entry age, drawn: a real class is not all teenagers — roughly three
    /// in ten enter at the youngest age, a quarter a year later, then a fifth, a seventh, and one in ten
    /// four years older (the four-year college player). An older prospect is generated at his own age,
    /// so his ceiling sits closer to what he already is: more ready, less upside.
    /// </summary>
    /// <summary>
    /// Rating points of polish an older prospect carries per year over the youngest: four years of college
    /// basketball make a more finished player now, while his age keeps his ceiling nearer to it.
    /// </summary>
    private const int PolishPerYearOlder = 2;

    private static int AgeOffset(IRandomSource random)
    {
        var draw = random.NextInt32(0, 100);
        return draw switch
        {
            < 30 => 0,
            < 55 => 1,
            < 75 => 2,
            < 90 => 3,
            _ => 4,
        };
    }

    private static int GenerateTalent(DraftClassRules rules, IRandomSource random)
    {
        var first = random.NextInt32(rules.MinimumRating, rules.MaximumRating + 1);
        var second = random.NextInt32(rules.MinimumRating, rules.MaximumRating + 1);
        return (first + second) / 2;
    }
}
