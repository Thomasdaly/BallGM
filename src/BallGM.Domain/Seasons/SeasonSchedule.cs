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

    private SeasonSchedule(List<Fixture> fixtures)
    {
        _fixtures = fixtures;
        _byId = fixtures.ToDictionary(fixture => fixture.Id.Value, StringComparer.Ordinal);
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
        return _fixtures.Where(fixture => fixture.Day == day).ToArray();
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

    /// <summary>A schedule with more fixtures added — used when the postseason bracket is drawn.</summary>
    public DomainOperationResult<SeasonSchedule> With(IEnumerable<Fixture> additional)
    {
        ArgumentNullException.ThrowIfNull(additional);
        return Create(_fixtures.Concat(additional));
    }
}
