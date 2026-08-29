using BallGM.Domain.Common;
using BallGM.Domain.Leagues;
using BallGM.Domain.Negotiations;
using BallGM.Domain.Randomness;
using BallGM.Domain.Seasons;
using BallGM.Domain.Teams;
using BallGM.Rules.Configuration;

namespace BallGM.Rules.Seasons;

/// <summary>
/// A generated fixture list, with everything the rules had to say about generating it.
/// <para>
/// <see cref="Notes"/> carries the rules this league does not configure — an absent opponent
/// weighting, a weighting stated by a league with no groups to weight — and <see cref="Warnings"/>
/// carries the places the schedule could not be perfectly balanced, with the figures. Neither is a
/// failure: an odd number of teams cannot play a balanced schedule at all, and a league is entitled
/// to have one.
/// </para>
/// </summary>
public sealed record ScheduleGeneration(
    SeasonSchedule Schedule,
    IReadOnlyList<RuleFinding> Warnings,
    IReadOnlyList<RuleFinding> Notes,
    IReadOnlyDictionary<string, int> GamesPerTeam);

/// <summary>
/// Builds one season's regular-season fixtures: who plays whom, how often, and on which day.
/// <para>
/// Reproducible from the season's seed and nothing else. The seed decides the order teams enter the
/// rotation and the order meetings are packed into days; it decides nothing about <em>which</em>
/// meetings exist, because that is arithmetic on the alignment and the ruleset. Two runs of one
/// season therefore produce the identical fixture list — which is the precondition for the whole
/// determinism claim, since every game's random stream is derived from its identifier and its
/// identifier is derived from the day and slot the generator put it in.
/// </para>
/// </summary>
public sealed class ScheduleGenerator
{
    private const string TooFewTeamsCode = "schedule.too_few_teams";
    private const string NotEnoughDaysCode = "schedule.not_enough_days";
    private const string NoRegularSeasonCode = "schedule.no_regular_season_phase";
    private const string NoWeightingCode = "schedule.opponent_weighting_not_configured";
    private const string WeightingWithoutGroupsCode = "schedule.opponent_weighting_without_groups";
    private const string UnalignedTeamsCode = "schedule.teams_outside_alignment";
    private const string UnbalancedTeamCountCode = "schedule.unbalanced_team_count";
    private const string GameCountDisagreesCode = "schedule.weighting_disagrees_with_game_count";

    public DomainOperationResult<ScheduleGeneration> Generate(
        Season season,
        League league,
        LeagueCalendar calendar,
        ScheduleRules scheduleRules,
        int regularSeasonGameCount,
        SeasonSeed seed)
    {
        ArgumentNullException.ThrowIfNull(season);
        ArgumentNullException.ThrowIfNull(league);
        ArgumentNullException.ThrowIfNull(calendar);
        ArgumentNullException.ThrowIfNull(scheduleRules);
        ArgumentNullException.ThrowIfNull(seed);

        var regularSeason = calendar.Phase(SeasonPhase.RegularSeason);
        if (regularSeason is null)
        {
            return DomainOperationResult<ScheduleGeneration>.Failure(new DomainError(
                NoRegularSeasonCode,
                $"Season {season.Year}'s calendar has no regular-season phase, so there are no days to play fixtures on."));
        }

        // Ordinal team order first, so the seeded shuffle below starts from a fixed arrangement.
        // Shuffling an order that was itself unstable would make the schedule depend on whichever
        // order the aggregate's set happened to enumerate in.
        var teams = league.TeamIds
            .OrderBy(teamId => teamId.Value, StringComparer.Ordinal)
            .ToArray();

        if (teams.Length < 2)
        {
            return DomainOperationResult<ScheduleGeneration>.Failure(new DomainError(
                TooFewTeamsCode,
                $"A league of {teams.Length} team(s) has nobody to play. A schedule needs at least two teams."));
        }

        if (regularSeasonGameCount <= 0)
        {
            return DomainOperationResult<ScheduleGeneration>.Failure(new DomainError(
                TooFewTeamsCode,
                $"This league's regular season is {regularSeasonGameCount} games long."));
        }

        var warnings = new List<RuleFinding>();
        var notes = new List<RuleFinding>();
        var random = new SeededRandomSource(seed.ForSchedule());

        var rotation = Shuffle(teams, random);
        var meetings = BuildMeetings(league, scheduleRules, rotation, regularSeasonGameCount, warnings, notes);

        // Home and away alternate through each pair's meetings, so the pair that meets an odd number
        // of times splits as evenly as it can and the extra home game goes to the team the ordinal
        // key puts first. Deterministic, and stated rather than left to whichever side was listed
        // first by an enumeration.
        var fixtures = PackIntoDays(season, meetings, regularSeason, random);

        if (fixtures is null)
        {
            return DomainOperationResult<ScheduleGeneration>.Failure(new DomainError(
                NotEnoughDaysCode,
                $"Season {season.Year} needs more than the {regularSeason.LengthInDays} day(s) its regular season runs for: {meetings.Count} game(s) across {teams.Length} teams cannot be fitted in without a team playing twice in a day."));
        }

        var scheduleResult = SeasonSchedule.Create(fixtures);
        if (scheduleResult.IsFailure)
        {
            return DomainOperationResult<ScheduleGeneration>.Failure(scheduleResult.Errors.ToArray());
        }

        var gamesPerTeam = teams.ToDictionary(
            teamId => teamId.Value,
            teamId => fixtures.Count(fixture => fixture.Involves(teamId)),
            StringComparer.Ordinal);

        var distinctCounts = gamesPerTeam.Values.Distinct().ToArray();
        if (distinctCounts.Length > 1)
        {
            warnings.Add(new RuleFinding(
                UnbalancedTeamCountCode,
                $"This league's {teams.Length} teams cannot all play the same number of games: the schedule gives between {distinctCounts.Min()} and {distinctCounts.Max()}. With an odd number of teams somebody sits out every round, so a perfectly balanced schedule does not exist — this is the closest one."));
        }
        else if (distinctCounts[0] != regularSeasonGameCount)
        {
            warnings.Add(new RuleFinding(
                GameCountDisagreesCode,
                $"The ruleset states a {regularSeasonGameCount}-game regular season, but the schedule this league's shape and weighting produce is {distinctCounts[0]} games per team. The more specific statement was honoured."));
        }

        return DomainOperationResult<ScheduleGeneration>.Success(new ScheduleGeneration(
            scheduleResult.Value,
            warnings,
            notes,
            gamesPerTeam));
    }

    /// <summary>
    /// How many times each unordered pair meets. Either the league's stated weighting, or — where it
    /// states none, or states one it has no groups to apply — a balanced rotation repeated until
    /// every team has the configured number of games.
    /// </summary>
    private static List<(TeamId Home, TeamId Away)> BuildMeetings(
        League league,
        ScheduleRules scheduleRules,
        IReadOnlyList<TeamId> rotation,
        int regularSeasonGameCount,
        List<RuleFinding> warnings,
        List<RuleFinding> notes)
    {
        var alignment = league.Alignment;
        var useWeighting = scheduleRules.HasOpponentWeighting;

        if (useWeighting && alignment.IsFlat)
        {
            notes.Add(new RuleFinding(
                WeightingWithoutGroupsCode,
                "This ruleset states how often each kind of opponent is played, but this league has no conferences or divisions for the weighting to apply to. A balanced rotation was used instead."));
            useWeighting = false;
        }

        if (!useWeighting && !scheduleRules.HasOpponentWeighting)
        {
            notes.Add(new RuleFinding(
                NoWeightingCode,
                "This league states no per-opponent weighting, so no rule decides that a division rival is played more often than anyone else. Every opponent is played the same number of times."));
        }

        if (useWeighting)
        {
            var unaligned = rotation.Where(teamId => alignment.ConferenceOf(teamId) is null).ToArray();
            if (unaligned.Length > 0)
            {
                notes.Add(new RuleFinding(
                    UnalignedTeamsCode,
                    $"{unaligned.Length} team(s) are not placed in any conference, so the weighting has no group to read for them. They are played at the cross-conference rate."));
            }

            return WeightedMeetings(alignment, scheduleRules, rotation);
        }

        return RotationMeetings(rotation, regularSeasonGameCount, warnings);
    }

    private static List<(TeamId Home, TeamId Away)> WeightedMeetings(
        LeagueAlignment alignment,
        ScheduleRules scheduleRules,
        IReadOnlyList<TeamId> rotation)
    {
        var meetings = new List<(TeamId, TeamId)>();

        for (var first = 0; first < rotation.Count; first++)
        {
            for (var second = first + 1; second < rotation.Count; second++)
            {
                var left = rotation[first];
                var right = rotation[second];

                var count = alignment.AreInSameDivision(left, right)
                    ? scheduleRules.GamesVersusDivisionOpponent!.Value
                    : alignment.AreInSameConference(left, right)
                        ? scheduleRules.GamesVersusConferenceOpponent!.Value
                        : scheduleRules.GamesVersusOtherConferenceOpponent!.Value;

                AddPairMeetings(meetings, left, right, count);
            }
        }

        return meetings;
    }

    /// <summary>
    /// A balanced rotation: whole cycles of the circle method until the configured game count is as
    /// close as it can be, then as many extra rounds as the remainder needs. Every team plays every
    /// other the same number of times inside a cycle, so the only imbalance possible is the one an
    /// odd team count forces.
    /// </summary>
    private static List<(TeamId Home, TeamId Away)> RotationMeetings(
        IReadOnlyList<TeamId> rotation,
        int regularSeasonGameCount,
        List<RuleFinding> warnings)
    {
        var opponentsPerCycle = rotation.Count - 1;
        var fullCycles = regularSeasonGameCount / opponentsPerCycle;
        var remainder = regularSeasonGameCount % opponentsPerCycle;

        var rounds = CircleMethodRounds(rotation);
        var meetings = new List<(TeamId, TeamId)>();
        var pairCounts = new Dictionary<(string, string), int>();

        for (var cycle = 0; cycle < fullCycles; cycle++)
        {
            foreach (var round in rounds)
            {
                foreach (var (left, right) in round)
                {
                    AppendMeeting(meetings, pairCounts, left, right);
                }
            }
        }

        // Each extra round adds one game to every team that is not sitting it out. With an even team
        // count nobody sits out, so the remainder lands exactly; with an odd count one team a round
        // does, which is the imbalance the caller reports with the figures.
        for (var extra = 0; extra < remainder && rounds.Count > 0; extra++)
        {
            foreach (var (left, right) in rounds[extra % rounds.Count])
            {
                AppendMeeting(meetings, pairCounts, left, right);
            }
        }

        if (fullCycles == 0 && remainder == 0)
        {
            warnings.Add(new RuleFinding(
                UnbalancedTeamCountCode,
                $"A {regularSeasonGameCount}-game season across {rotation.Count} teams works out at no complete rotation, so no fixtures could be generated."));
        }

        return meetings;
    }

    private static void AppendMeeting(
        List<(TeamId, TeamId)> meetings,
        Dictionary<(string, string), int> pairCounts,
        TeamId left,
        TeamId right)
    {
        var key = string.CompareOrdinal(left.Value, right.Value) <= 0
            ? (left.Value, right.Value)
            : (right.Value, left.Value);

        var alreadyPlayed = pairCounts.GetValueOrDefault(key);
        pairCounts[key] = alreadyPlayed + 1;

        // Alternate the venue through the pair's meetings, starting with the ordinally first team at
        // home so that the pattern is a stated rule rather than an artefact of enumeration order.
        var firstIsHome = alreadyPlayed % 2 == 0;
        var (home, away) = string.CompareOrdinal(left.Value, right.Value) <= 0
            ? (firstIsHome ? (left, right) : (right, left))
            : (firstIsHome ? (right, left) : (left, right));

        meetings.Add((home, away));
    }

    private static void AddPairMeetings(List<(TeamId, TeamId)> meetings, TeamId left, TeamId right, int count)
    {
        for (var index = 0; index < count; index++)
        {
            var firstIsHome = index % 2 == 0;
            meetings.Add(firstIsHome ? (left, right) : (right, left));
        }
    }

    /// <summary>
    /// The circle method: one round per rotation step, each round a set of pairings in which no team
    /// appears twice. An odd team count is padded with a bye, and the team drawn against the bye
    /// sits that round out — which is exactly why an odd league cannot be balanced.
    /// </summary>
    private static List<List<(TeamId Left, TeamId Right)>> CircleMethodRounds(IReadOnlyList<TeamId> rotation)
    {
        var participants = rotation.ToList();
        var hasBye = participants.Count % 2 == 1;

        if (hasBye)
        {
            participants.Add(null!);
        }

        var count = participants.Count;
        var rounds = new List<List<(TeamId, TeamId)>>(count - 1);

        for (var round = 0; round < count - 1; round++)
        {
            var pairings = new List<(TeamId, TeamId)>(count / 2);

            for (var slot = 0; slot < count / 2; slot++)
            {
                var left = participants[slot];
                var right = participants[count - 1 - slot];

                if (left is not null && right is not null)
                {
                    pairings.Add((left, right));
                }
            }

            rounds.Add(pairings);

            // Rotate every position but the first, which is what makes each team meet every other
            // exactly once across the full set of rounds.
            var last = participants[count - 1];
            for (var slot = count - 1; slot > 1; slot--)
            {
                participants[slot] = participants[slot - 1];
            }

            participants[1] = last;
        }

        return rounds;
    }

    /// <summary>
    /// Packs meetings into days, one game a day per team, filling each day as far as it will go
    /// before moving on. Returns null where the phase does not have enough days.
    /// </summary>
    private static List<Fixture>? PackIntoDays(
        Season season,
        List<(TeamId Home, TeamId Away)> meetings,
        CalendarPhase regularSeason,
        IRandomSource random)
    {
        var pending = Shuffle(meetings, random);
        var scheduled = new bool[pending.Count];
        var remaining = pending.Count;
        var fixtures = new List<Fixture>(pending.Count);

        for (var dayIndex = regularSeason.StartDay.Index; dayIndex < regularSeason.EndDayExclusive.Index && remaining > 0; dayIndex++)
        {
            var day = new SeasonDay(dayIndex);
            var busy = new HashSet<string>(StringComparer.Ordinal);
            var slot = 0;

            for (var index = 0; index < pending.Count && remaining > 0; index++)
            {
                if (scheduled[index])
                {
                    continue;
                }

                var (home, away) = pending[index];
                if (busy.Contains(home.Value) || busy.Contains(away.Value))
                {
                    continue;
                }

                busy.Add(home.Value);
                busy.Add(away.Value);
                scheduled[index] = true;
                remaining--;

                fixtures.Add(new Fixture(GameId.For(season, day, slot), day, home, away, SeasonPhase.RegularSeason));
                slot++;
            }
        }

        return remaining > 0 ? null : fixtures;
    }

    /// <summary>
    /// A seeded Fisher–Yates shuffle. Written out rather than taken from a library so the sequence
    /// of draws is fixed by this code and this seed, on every platform and every runtime version.
    /// </summary>
    private static List<T> Shuffle<T>(IReadOnlyList<T> source, IRandomSource random)
    {
        var items = source.ToList();

        for (var index = items.Count - 1; index > 0; index--)
        {
            var swapWith = random.NextInt32(0, index + 1);
            (items[index], items[swapWith]) = (items[swapWith], items[index]);
        }

        return items;
    }
}
