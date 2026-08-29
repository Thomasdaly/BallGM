using BallGM.Domain.Common;
using BallGM.Domain.Teams;

namespace BallGM.Domain.Leagues;

/// <summary>
/// One division: a name and the teams in it. The smallest group a schedule or a standings tie-break
/// can be expressed against.
/// </summary>
public sealed record LeagueDivision
{
    public LeagueDivision(string name, IEnumerable<TeamId> teamIds)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(teamIds);

        var ordered = teamIds.ToArray();
        if (ordered.Any(teamId => teamId is null))
        {
            throw new ArgumentException("A division cannot contain null team identifiers.", nameof(teamIds));
        }

        Name = name;
        TeamIds = ordered;
    }

    public string Name { get; }

    public IReadOnlyList<TeamId> TeamIds { get; }
}

/// <summary>One conference: a name and the divisions inside it.</summary>
public sealed record LeagueConference
{
    public LeagueConference(string name, IEnumerable<LeagueDivision> divisions)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(divisions);

        var ordered = divisions.ToArray();
        if (ordered.Any(division => division is null))
        {
            throw new ArgumentException("A conference cannot contain null divisions.", nameof(divisions));
        }

        Name = name;
        Divisions = ordered;
    }

    public string Name { get; }

    public IReadOnlyList<LeagueDivision> Divisions { get; }

    public IEnumerable<TeamId> TeamIds => Divisions.SelectMany(division => division.TeamIds);
}

/// <summary>
/// How a league's teams are grouped into conferences and divisions.
/// <para>
/// This is league <em>content</em>, not a rule, which is why it sits on the <see cref="League"/>
/// aggregate rather than in the ruleset. Who plays in which division moves when a franchise
/// relocates or the league expands (Milestone 13); how many times you play a division opponent is
/// the part that is configured, and that lives in the ruleset's schedule rules. Keeping the two
/// apart is what stops an expansion from being a ruleset edit.
/// </para>
/// <para>
/// A league may have no alignment at all — <see cref="Flat"/> — and that is a real league rather
/// than a missing one: every team is simply an opponent like any other, and the schedule generator
/// falls back to a balanced round robin. It is also what a standings tie-break keyed on division
/// record has to be able to report as "this league has no divisions" rather than silently pass.
/// </para>
/// </summary>
public sealed class LeagueAlignment
{
    private const string DuplicateConferenceNameCode = "alignment.duplicate_conference_name";
    private const string DuplicateDivisionNameCode = "alignment.duplicate_division_name";
    private const string DuplicateTeamCode = "alignment.team_in_more_than_one_division";
    private const string EmptyDivisionCode = "alignment.empty_division";
    private const string EmptyConferenceCode = "alignment.empty_conference";

    private readonly Dictionary<TeamId, string> _conferenceByTeam;
    private readonly Dictionary<TeamId, string> _divisionByTeam;

    private LeagueAlignment(IReadOnlyList<LeagueConference> conferences)
    {
        Conferences = conferences;
        _conferenceByTeam = [];
        _divisionByTeam = [];

        foreach (var conference in conferences)
        {
            foreach (var division in conference.Divisions)
            {
                foreach (var teamId in division.TeamIds)
                {
                    _conferenceByTeam[teamId] = conference.Name;
                    _divisionByTeam[teamId] = division.Name;
                }
            }
        }
    }

    /// <summary>A league with no conferences and no divisions. Every opponent is the same kind of opponent.</summary>
    public static LeagueAlignment Flat { get; } = new([]);

    /// <summary>
    /// Builds an alignment, refusing a team that appears twice or a group with nothing in it.
    /// A structured failure rather than a throw: alignment comes from a data pack.
    /// </summary>
    public static DomainOperationResult<LeagueAlignment> Create(IEnumerable<LeagueConference> conferences)
    {
        ArgumentNullException.ThrowIfNull(conferences);

        var ordered = conferences.ToArray();
        if (ordered.Any(conference => conference is null))
        {
            throw new ArgumentException("An alignment cannot contain null conferences.", nameof(conferences));
        }

        if (ordered.Length == 0)
        {
            return DomainOperationResult<LeagueAlignment>.Success(Flat);
        }

        var errors = new List<DomainError>();
        var conferenceNames = new HashSet<string>(StringComparer.Ordinal);
        var divisionNames = new HashSet<string>(StringComparer.Ordinal);
        var placedTeams = new HashSet<TeamId>();

        foreach (var conference in ordered)
        {
            if (!conferenceNames.Add(conference.Name))
            {
                errors.Add(new DomainError(
                    DuplicateConferenceNameCode,
                    $"Conference '{conference.Name}' is named twice. A conference name is how a standings screen and a tie-break rule refer to it, so it has to be unique."));
            }

            if (conference.Divisions.Count == 0)
            {
                errors.Add(new DomainError(
                    EmptyConferenceCode,
                    $"Conference '{conference.Name}' has no divisions. A conference with no teams under it cannot be seeded or scheduled."));
            }

            foreach (var division in conference.Divisions)
            {
                if (!divisionNames.Add(division.Name))
                {
                    errors.Add(new DomainError(
                        DuplicateDivisionNameCode,
                        $"Division '{division.Name}' is named twice. Division names are unique across the whole league, not just within a conference."));
                }

                if (division.TeamIds.Count == 0)
                {
                    errors.Add(new DomainError(
                        EmptyDivisionCode,
                        $"Division '{division.Name}' has no teams in it."));
                }

                foreach (var teamId in division.TeamIds.Where(teamId => !placedTeams.Add(teamId)))
                {
                    errors.Add(new DomainError(
                        DuplicateTeamCode,
                        $"Team '{teamId.Value}' is in more than one division. A team plays out of exactly one."));
                }
            }
        }

        return errors.Count > 0
            ? DomainOperationResult<LeagueAlignment>.Failure(errors.ToArray())
            : DomainOperationResult<LeagueAlignment>.Success(new LeagueAlignment(ordered));
    }

    public IReadOnlyList<LeagueConference> Conferences { get; }

    /// <summary>Whether this league groups its teams at all.</summary>
    public bool IsFlat => Conferences.Count == 0;

    public bool HasDivisions => Conferences.Any(conference => conference.Divisions.Count > 0);

    public IReadOnlyCollection<TeamId> AlignedTeamIds => _conferenceByTeam.Keys;

    public string? ConferenceOf(TeamId teamId)
    {
        ArgumentNullException.ThrowIfNull(teamId);
        return _conferenceByTeam.GetValueOrDefault(teamId);
    }

    public string? DivisionOf(TeamId teamId)
    {
        ArgumentNullException.ThrowIfNull(teamId);
        return _divisionByTeam.GetValueOrDefault(teamId);
    }

    /// <summary>
    /// False in a flat league, and false for a team the alignment does not place — an unplaced team
    /// shares a group with nobody rather than sharing "no group" with every other unplaced team.
    /// </summary>
    public bool AreInSameConference(TeamId left, TeamId right)
    {
        var leftConference = ConferenceOf(left);
        return leftConference is not null && string.Equals(leftConference, ConferenceOf(right), StringComparison.Ordinal);
    }

    public bool AreInSameDivision(TeamId left, TeamId right)
    {
        var leftDivision = DivisionOf(left);
        return leftDivision is not null && string.Equals(leftDivision, DivisionOf(right), StringComparison.Ordinal);
    }

    public IReadOnlyList<TeamId> TeamsIn(string conferenceName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(conferenceName);

        return Conferences
            .Where(conference => string.Equals(conference.Name, conferenceName, StringComparison.Ordinal))
            .SelectMany(conference => conference.TeamIds)
            .ToArray();
    }
}
