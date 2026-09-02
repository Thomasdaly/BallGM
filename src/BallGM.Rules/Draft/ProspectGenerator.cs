using BallGM.Domain.Common;
using BallGM.Domain.Draft;
using BallGM.Domain.Leagues;
using BallGM.Domain.Players;
using BallGM.Domain.Randomness;
using BallGM.Rules.Configuration;

namespace BallGM.Rules.Draft;

/// <summary>
/// Builds one season's <see cref="DraftClass"/> from <see cref="DraftClassRules"/> rather than from
/// hardcoded content — the size of the class, the spread of true rating it draws from, and the age
/// every prospect enters at are all ruleset data, the same pattern <c>ScheduleGenerator</c> already
/// follows for the regular season. Deterministic: the same rules and the same <see cref="IRandomSource"/>
/// state produce the same class, on every platform and every run.
/// <para>
/// A prospect's true rating is the average of two draws inside <see cref="DraftClassRules.MinimumRating"/>
/// and <see cref="DraftClassRules.MaximumRating"/> rather than one uniform draw, so most classes cluster
/// mid-range and a prospect at the very top or bottom of the stated spread stays rare — a shape a
/// single uniform draw does not have. Position is assigned round-robin across the five positions
/// rather than drawn, so a class of any size still fields a roughly even spread rather than risking an
/// all-guard class on an unlucky seed; the position generation itself is exactly as configurable as
/// that decision needs to be, which is not at all, until a league wants to weight it.
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

        var birthDate = new DateOnly(draftSeason.Year - rules.ProspectAgeYears, 7, 1);
        var prospects = new List<Prospect>(rules.ClassSize);
        var positions = Enum.GetValues<Position>();

        for (var index = 0; index < rules.ClassSize; index++)
        {
            var position = positions[index % positions.Length];
            var overall = GenerateOverall(rules, random);
            var name = ProspectNameBank.NextName(random);

            var prospectResult = Prospect.Create(
                new ProspectId(SortableId.NewId()),
                name,
                position,
                birthDate,
                new PlayerRating(overall));

            if (prospectResult.IsFailure)
            {
                return DomainOperationResult<DraftClass>.Failure(prospectResult.Errors.ToArray());
            }

            prospects.Add(prospectResult.Value);
        }

        return DraftClass.Create(id, draftSeason, prospects);
    }

    private static int GenerateOverall(DraftClassRules rules, IRandomSource random)
    {
        var first = random.NextInt32(rules.MinimumRating, rules.MaximumRating + 1);
        var second = random.NextInt32(rules.MinimumRating, rules.MaximumRating + 1);
        return (first + second) / 2;
    }
}
