using BallGM.Domain.Common;
using BallGM.Domain.Seasons;
using BallGM.Domain.Teams;

namespace BallGM.Domain.Leagues;

/// <summary>
/// Aggregate root for league identity and league-level team membership.
/// Team roster membership is owned by the Team aggregate and referenced by identifier.
/// </summary>
public sealed class League
{
    private const string DuplicateTeamCode = "league.duplicate_team_membership";
    private const string AlignmentTeamNotInLeagueCode = "league.alignment_team_not_in_league";
    private const string SeasonAlreadyConcludedCode = "league.season_already_concluded";

    private readonly HashSet<TeamId> _teamIds;
    private readonly List<SeasonHistoryEntry> _history = [];

    private League(LeagueId id, string name, IReadOnlyCollection<TeamId> teamIds, LeagueAlignment alignment)
    {
        Id = id;
        Name = name;
        _teamIds = new HashSet<TeamId>(teamIds);
        Alignment = alignment;
    }

    /// <summary>
    /// Creates a league, validating structural arguments by throwing (a caller/programming
    /// error) and membership rules by returning a structured failure (a business rule that
    /// untrusted data-pack content can legitimately violate).
    /// <para>
    /// <paramref name="alignment"/> is optional and defaults to <see cref="LeagueAlignment.Flat"/>.
    /// A flat league is a real league, not a misconfigured one: every opponent is the same kind of
    /// opponent, and the schedule falls back to a balanced round robin.
    /// </para>
    /// </summary>
    public static DomainOperationResult<League> Create(
        LeagueId id,
        string name,
        IEnumerable<TeamId> teamIds,
        LeagueAlignment? alignment = null)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(teamIds);

        var teamIdList = teamIds.ToArray();
        if (teamIdList.Any(teamId => teamId is null))
        {
            throw new ArgumentException("League team identifiers cannot contain null values.", nameof(teamIds));
        }

        var errors = new List<DomainError>();

        if (teamIdList.Length != teamIdList.Distinct().Count())
        {
            errors.Add(new DomainError(DuplicateTeamCode, "League team identifiers must be unique."));
        }

        var resolvedAlignment = alignment ?? LeagueAlignment.Flat;
        var members = new HashSet<TeamId>(teamIdList);

        // An alignment naming a team the league does not have would put a phantom in a division,
        // and every schedule and standings table built from it would be quietly wrong about how
        // many opponents a group has. Cheaper to refuse at load than to explain later.
        foreach (var strayTeamId in resolvedAlignment.AlignedTeamIds.Where(teamId => !members.Contains(teamId)))
        {
            errors.Add(new DomainError(
                AlignmentTeamNotInLeagueCode,
                $"The alignment places team '{strayTeamId.Value}' in a division, but that team is not in this league."));
        }

        return errors.Count > 0
            ? DomainOperationResult<League>.Failure(errors.ToArray())
            : DomainOperationResult<League>.Success(new League(id, name, teamIdList, resolvedAlignment));
    }

    public LeagueId Id { get; }

    public string Name { get; }

    public IReadOnlyCollection<TeamId> TeamIds => _teamIds.ToArray();

    /// <summary>
    /// How this league's teams are grouped. <see cref="LeagueAlignment.Flat"/> in a league that does
    /// not group them, which the schedule generator and the tie-break resolver both have to be able
    /// to say out loud rather than assume away.
    /// </summary>
    public LeagueAlignment Alignment { get; }

    /// <summary>Every season this league has concluded, oldest first.</summary>
    public IReadOnlyList<SeasonHistoryEntry> History => _history;

    /// <summary>
    /// Archives a concluded season. Refuses a second entry for a season year already recorded, which
    /// is what makes concluding the same season twice a read-only refusal rather than a duplicate
    /// archived table — see <c>BallGM.Rules.Seasons.SeasonConclusion</c>, the only caller.
    /// </summary>
    public DomainOperationResult RecordSeason(SeasonHistoryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (_history.Any(recorded => recorded.Season.Year == entry.Season.Year))
        {
            return DomainOperationResult.Failure(new DomainError(
                SeasonAlreadyConcludedCode,
                $"Season {entry.Season.Year} has already been concluded and archived in this league's history."));
        }

        _history.Add(entry);
        return DomainOperationResult.Success;
    }
}
