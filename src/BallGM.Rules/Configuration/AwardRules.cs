using BallGM.Domain.Common;

namespace BallGM.Rules.Configuration;

/// <summary>The counting stat one award ranks its candidates by — the same four <c>PlayerStatLine</c> already tracks.</summary>
public enum AwardStatBasis
{
    TotalPoints = 1,
    TotalRebounds = 2,
    TotalAssists = 3,
    TotalMinutes = 4,
}

/// <summary>
/// What an award recognises, and so how its candidates are scored (see
/// <c>BallGM.Rules.Seasons.SeasonAwardCalculator</c>) and who may be a candidate at all.
/// </summary>
public enum AwardKind
{
    /// <summary>Whoever leads the league in a stated total (<see cref="AwardDefinition.StatBasis"/>).</summary>
    StatLeader = 0,

    /// <summary>The season's most valuable player: production a game, weighted by how his team did.</summary>
    MostValuable = 1,

    /// <summary>The best defender: steals, blocks and defensive boards a game, and how his team defended.</summary>
    Defensive = 2,

    /// <summary>The best player off the bench — only players who came off the bench in more games than they started.</summary>
    Bench = 3,

    /// <summary>The best first-season player.</summary>
    Rookie = 4,

    /// <summary>The player whose production a game rose most on his previous season.</summary>
    MostImproved = 5,

    /// <summary>Teams of five, position-less, by the most-valuable score (<see cref="AwardDefinition.Teams"/> of them).</summary>
    AllLeague = 6,

    /// <summary>Teams of five by the defensive score.</summary>
    AllDefensive = 7,
}

/// <summary>
/// One award this league hands out: its code, its display name, what it recognises
/// (<see cref="Kind"/>), and who is eligible. A bare record with no factory of its own, validated by
/// <see cref="AwardRules.Create"/> the way <c>DraftOrderSlot</c> is validated by the snapshot that
/// contains it — an award only means anything in the context of the full list, where its code has to
/// be unique.
/// <para>
/// <b>Eligibility is a rule, not a side effect of the score.</b> A player must have played at least
/// <see cref="MinimumMinutesPerGame"/> minutes in at least <see cref="MinimumGamesPercent"/> per cent
/// of the season's games to be a candidate at all — the shape of the real league's games-played floor
/// for its major awards — and the bench award also requires more games off the bench than started. This
/// is what stops a team's best player, who starts every night, from ever winning the bench award.
/// </para>
/// </summary>
public sealed record AwardDefinition(string Code, string Name, AwardStatBasis? StatBasis)
{
    /// <summary>What the award recognises. A definition that only names a stat basis is a stat-leader award.</summary>
    public AwardKind Kind { get; init; } = AwardKind.StatLeader;

    /// <summary>The share of the season's games, as a whole percentage, a candidate must have qualified in (0 for none).</summary>
    public int MinimumGamesPercent { get; init; }

    /// <summary>The minutes a game must reach to count toward <see cref="MinimumGamesPercent"/> (0 counts every appearance).</summary>
    public int MinimumMinutesPerGame { get; init; }

    /// <summary>How many teams of five an all-league or all-defensive award names; 1 for every other kind.</summary>
    public int Teams { get; init; } = 1;
}

/// <summary>
/// Which awards this league hands out, and how each is decided — data, not code, so a modded league
/// with no defensive award needs no code change to leave it out, and one that wants an award this
/// build never shipped only needs a new entry once the stat it ranks by exists.
/// <para>
/// "How they are voted" is a stated, explainable score per <see cref="AwardKind"/> over the season's
/// box scores and table, inside an eligibility rule (see <see cref="AwardDefinition"/>) — not a
/// simulated media panel. The score weighs more than one number, the way a real vote does (production,
/// availability, team success), and every winner is reported with the figures that decided it.
/// </para>
/// </summary>
public sealed record AwardRules
{
    private const string EmptyCodeCode = "ruleset.award_missing_code";
    private const string EmptyNameCode = "ruleset.award_missing_name";
    private const string DuplicateCodeCode = "ruleset.award_duplicate_code";
    private const string MissingStatBasisCode = "ruleset.award_missing_stat_basis";
    private const string InvalidEligibilityCode = "ruleset.award_invalid_eligibility";
    private const string InvalidTeamsCode = "ruleset.award_invalid_teams";

    /// <summary>The most teams of five one award may name.</summary>
    public const int MaximumTeams = 5;

    private AwardRules(IReadOnlyList<AwardDefinition> awards) => Awards = awards;

    /// <summary>A league that hands out no awards at all.</summary>
    public static AwardRules None { get; } = new([]);

    public bool IsConfigured => Awards.Count > 0;

    public IReadOnlyList<AwardDefinition> Awards { get; }

    public static DomainOperationResult<AwardRules> Create(IEnumerable<AwardDefinition>? awards)
    {
        if (awards is null)
        {
            return DomainOperationResult<AwardRules>.Success(None);
        }

        var list = awards.ToArray();
        if (list.Any(award => award is null))
        {
            throw new ArgumentException("The award list cannot contain a null award.", nameof(awards));
        }

        if (list.Length == 0)
        {
            return DomainOperationResult<AwardRules>.Success(None);
        }

        var errors = new List<DomainError>();

        foreach (var award in list)
        {
            if (string.IsNullOrWhiteSpace(award.Code))
            {
                errors.Add(new DomainError(EmptyCodeCode, $"The award '{award.Name}' has no code."));
            }

            if (string.IsNullOrWhiteSpace(award.Name))
            {
                errors.Add(new DomainError(EmptyNameCode, $"The award '{award.Code}' has no display name."));
            }

            if (award.Kind == AwardKind.StatLeader && award.StatBasis is null)
            {
                errors.Add(new DomainError(MissingStatBasisCode, $"The award '{award.Code}' is a stat-leader award but names no stat to lead in."));
            }

            if (award.MinimumGamesPercent is < 0 or > 100)
            {
                errors.Add(new DomainError(InvalidEligibilityCode, $"The award '{award.Code}' asks for {award.MinimumGamesPercent}% of games; a share is 0 to 100."));
            }

            if (award.MinimumMinutesPerGame is < 0 or > 48)
            {
                errors.Add(new DomainError(InvalidEligibilityCode, $"The award '{award.Code}' asks for {award.MinimumMinutesPerGame} minutes a game; a game has 0 to 48."));
            }

            var namesTeams = award.Kind is AwardKind.AllLeague or AwardKind.AllDefensive;
            if (award.Teams < 1 || (!namesTeams && award.Teams != 1) || award.Teams > MaximumTeams)
            {
                errors.Add(new DomainError(InvalidTeamsCode, namesTeams
                    ? $"The award '{award.Code}' names {award.Teams} teams; it may name 1 to {MaximumTeams}."
                    : $"The award '{award.Code}' goes to one player and cannot name {award.Teams} teams."));
            }
        }

        var duplicate = list
            .Where(award => !string.IsNullOrWhiteSpace(award.Code))
            .GroupBy(award => award.Code, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);

        if (duplicate is not null)
        {
            errors.Add(new DomainError(DuplicateCodeCode, $"Award code '{duplicate.Key}' is stated more than once."));
        }

        return errors.Count > 0
            ? DomainOperationResult<AwardRules>.Failure(errors.ToArray())
            : DomainOperationResult<AwardRules>.Success(new AwardRules(list));
    }
}
