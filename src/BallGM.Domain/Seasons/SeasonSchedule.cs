using BallGM.Domain.Common;
using BallGM.Domain.Negotiations;
using BallGM.Domain.Teams;

namespace BallGM.Domain.Seasons;

/// <summary>
/// Every fixture in one season, in play order.
/// <para>
/// Order is <c>(day, game identifier)</c> ordinal ascending and is fixed at construction, so the
/// order games are simulated in is a property of the schedule rather than of whichever collection
/// happened to enumerate it. That matters more here than anywhere else in the codebase: two runs of
/// one season have to produce the same box scores, and a simulation loop that read its fixtures in
/// a different order would produce the same games with different fatigue behind them.
/// </para>
/// </summary>
public sealed class SeasonSchedule
{
    private const string DuplicateGameCode = "schedule.duplicate_game_id";
    private const string TeamPlaysTwiceCode = "schedule.team_plays_twice_on_one_day";

    private readonly List<Fixture> _fixtures;
    private readonly Dictionary<string, Fixture> _byId;
    private readonly Dictionary<int, IReadOnlyList<Fixture>> _byDay;
    private readonly Dictionary<string, int[]> _daysByTeam;

    private SeasonSchedule(List<Fixture> fixtures)
    {
        _fixtures = fixtures;
        _byId = fixtures.ToDictionary(fixture => fixture.Id.Value, StringComparer.Ordinal);

        // A schedule never changes after it is built (an extension builds a new one), so the two
        // lookups a season asks of it every game — a day's fixtures, a team's last game — are indexed
        // once here rather than answered by scanning every fixture each time.
        _byDay = fixtures.GroupBy(fixture => fixture.Day.Index)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<Fixture>)Array.AsReadOnly(group.ToArray()));
        _daysByTeam = fixtures
            .SelectMany(fixture => new[] { (Team: fixture.HomeTeamId.Value, Day: fixture.Day.Index), (Team: fixture.AwayTeamId.Value, Day: fixture.Day.Index) })
            .GroupBy(entry => entry.Team, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Select(entry => entry.Day).Distinct().Order().ToArray(), StringComparer.Ordinal);
    }

    private SeasonSchedule(
        List<Fixture> fixtures,
        Dictionary<string, Fixture> byId,
        Dictionary<int, IReadOnlyList<Fixture>> byDay,
        Dictionary<string, int[]> daysByTeam)
    {
        _fixtures = fixtures;
        _byId = byId;
        _byDay = byDay;
        _daysByTeam = daysByTeam;
    }

    /// <summary>A season with no fixtures at all. What a league that plays no games has.</summary>
    public static SeasonSchedule Empty { get; } = new([]);

    public static DomainOperationResult<SeasonSchedule> Create(IEnumerable<Fixture> fixtures)
    {
        ArgumentNullException.ThrowIfNull(fixtures);

        var supplied = fixtures.ToList();
        if (supplied.Any(fixture => fixture is null))
        {
            throw new ArgumentException("A schedule cannot contain null fixtures.", nameof(fixtures));
        }

        var errors = new List<DomainError>();
        var seenIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var fixture in supplied.Where(fixture => !seenIds.Add(fixture.Id.Value)))
        {
            errors.Add(new DomainError(
                DuplicateGameCode,
                $"Game '{fixture.Id.Value}' appears twice in the schedule. A game identifier is derived from the day and slot it occupies, so two of them means two games in one slot."));
        }

        foreach (var group in supplied.GroupBy(fixture => fixture.Day.Index))
        {
            var playing = new HashSet<TeamId>();

            foreach (var teamId in group.SelectMany<Fixture, TeamId>(fixture => [fixture.HomeTeamId, fixture.AwayTeamId])
                         .Where(teamId => !playing.Add(teamId))
                         .Distinct())
            {
                errors.Add(new DomainError(
                    TeamPlaysTwiceCode,
                    $"Team '{teamId.Value}' is scheduled to play twice on day {group.Key}. Fatigue, availability, and a box score all assume one game a day per team."));
            }
        }

        if (errors.Count > 0)
        {
            return DomainOperationResult<SeasonSchedule>.Failure(errors.ToArray());
        }

        var ordered = supplied
            .OrderBy(fixture => fixture.Day.Index)
            .ThenBy(fixture => fixture.Id.Value, StringComparer.Ordinal)
            .ToList();

        return DomainOperationResult<SeasonSchedule>.Success(new SeasonSchedule(ordered));
    }

    public IReadOnlyList<Fixture> Fixtures => _fixtures;

    public int Count => _fixtures.Count;

    public bool IsEmpty => _fixtures.Count == 0;

    public Fixture? Game(GameId id)
    {
        ArgumentNullException.ThrowIfNull(id);
        return _byId.GetValueOrDefault(id.Value);
    }

    public IReadOnlyList<Fixture> On(SeasonDay day)
    {
        ArgumentNullException.ThrowIfNull(day);
        return _byDay.TryGetValue(day.Index, out var fixtures) ? fixtures : [];
    }

    /// <summary>The day of this team's last scheduled game before <paramref name="day"/>, or null if it has none.</summary>
    public int? PreviousGameDay(TeamId teamId, SeasonDay day)
    {
        ArgumentNullException.ThrowIfNull(teamId);
        ArgumentNullException.ThrowIfNull(day);

        if (!_daysByTeam.TryGetValue(teamId.Value, out var days))
        {
            return null;
        }

        // The first scheduled day at or after this one (days are distinct); the game before it is the last one before.
        var at = Array.BinarySearch(days, day.Index);
        var firstNotBefore = at >= 0 ? at : ~at;
        return firstNotBefore == 0 ? null : days[firstNotBefore - 1];
    }

    /// <summary>Every fixture from <paramref name="fromInclusive"/> up to but not including <paramref name="toExclusive"/>.</summary>
    public IReadOnlyList<Fixture> Between(SeasonDay fromInclusive, SeasonDay toExclusive)
    {
        ArgumentNullException.ThrowIfNull(fromInclusive);
        ArgumentNullException.ThrowIfNull(toExclusive);

        return _fixtures.Where(fixture => fixture.Day >= fromInclusive && fixture.Day < toExclusive).ToArray();
    }

    public IReadOnlyList<Fixture> For(TeamId teamId)
    {
        ArgumentNullException.ThrowIfNull(teamId);
        return _fixtures.Where(fixture => fixture.Involves(teamId)).ToArray();
    }

    public int GameCountFor(TeamId teamId, SeasonPhase phase)
    {
        ArgumentNullException.ThrowIfNull(teamId);
        return _fixtures.Count(fixture => fixture.Phase == phase && fixture.Involves(teamId));
    }

    /// <summary>
    /// A schedule with more fixtures added — used when the postseason bracket is drawn, a game at a
    /// time. Only the added fixtures are checked against the indexes and merged into the order, rather
    /// than the whole season rebuilt for every postseason game; anything that would be refused goes
    /// through <see cref="Create"/> so the refusal reads exactly as it always has.
    /// </summary>
    public DomainOperationResult<SeasonSchedule> With(IEnumerable<Fixture> additional)
    {
        ArgumentNullException.ThrowIfNull(additional);

        var added = additional.ToList();
        if (added.Any(fixture => fixture is null) || !CanAddCleanly(added))
        {
            return Create(_fixtures.Concat(added));
        }

        added.Sort(PlayOrder);
        var merged = new List<Fixture>(_fixtures.Count + added.Count);
        var existing = 0;
        foreach (var fixture in added)
        {
            while (existing < _fixtures.Count && PlayOrder(_fixtures[existing], fixture) <= 0)
            {
                merged.Add(_fixtures[existing++]);
            }

            merged.Add(fixture);
        }

        merged.AddRange(_fixtures.Skip(existing));

        var byId = new Dictionary<string, Fixture>(_byId, StringComparer.Ordinal);
        var byDay = new Dictionary<int, IReadOnlyList<Fixture>>(_byDay);
        var daysByTeam = new Dictionary<string, int[]>(_daysByTeam, StringComparer.Ordinal);
        foreach (var fixture in added)
        {
            byId.Add(fixture.Id.Value, fixture);
        }

        foreach (var day in added.Select(fixture => fixture.Day.Index).Distinct())
        {
            byDay[day] = Array.AsReadOnly(merged.Where(fixture => fixture.Day.Index == day).ToArray());
        }

        foreach (var fixture in added)
        {
            foreach (var team in new[] { fixture.HomeTeamId.Value, fixture.AwayTeamId.Value })
            {
                var days = daysByTeam.GetValueOrDefault(team) ?? [];
                daysByTeam[team] = days.Contains(fixture.Day.Index) ? days : [.. days.Append(fixture.Day.Index).Order()];
            }
        }

        return DomainOperationResult<SeasonSchedule>.Success(new SeasonSchedule(merged, byId, byDay, daysByTeam));
    }

    /// <summary>Whether these fixtures join this schedule without a duplicate game or a team playing twice in a day.</summary>
    private bool CanAddCleanly(IReadOnlyList<Fixture> added)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var playing = new HashSet<(int Day, string Team)>();
        foreach (var fixture in added)
        {
            if (_byId.ContainsKey(fixture.Id.Value) || !ids.Add(fixture.Id.Value))
            {
                return false;
            }

            foreach (var team in new[] { fixture.HomeTeamId.Value, fixture.AwayTeamId.Value })
            {
                var alreadyThatDay = _daysByTeam.TryGetValue(team, out var days) && Array.BinarySearch(days, fixture.Day.Index) >= 0;
                if (alreadyThatDay || !playing.Add((fixture.Day.Index, team)))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static int PlayOrder(Fixture left, Fixture right)
    {
        var byDay = left.Day.Index.CompareTo(right.Day.Index);
        return byDay != 0 ? byDay : string.CompareOrdinal(left.Id.Value, right.Id.Value);
    }
}
