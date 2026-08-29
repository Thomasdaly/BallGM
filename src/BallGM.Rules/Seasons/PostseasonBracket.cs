using BallGM.Domain.Common;
using BallGM.Domain.Leagues;
using BallGM.Domain.Negotiations;
using BallGM.Domain.Seasons;
using BallGM.Domain.Teams;
using BallGM.Rules.Configuration;

namespace BallGM.Rules.Seasons;

/// <summary>One team's place in the bracket: where it qualified from, and how high.</summary>
/// <param name="Seed">
/// Its rank inside the group it qualified from, counted from 1. What the home-court sequence means
/// by "the higher seed" inside one conference.
/// </param>
/// <param name="LeaguePosition">
/// Its place in the league-wide table. The single key every "who is the higher seed" comparison
/// uses, because a final is contested between two conferences and a conference seed number cannot
/// separate two number-ones. Inside a conference it agrees with <paramref name="Seed"/> by
/// construction — both are read off the same ordered table.
/// </param>
public sealed record PostseasonSeed(TeamId TeamId, string? ConferenceName, int Seed, int LeaguePosition);

/// <summary>
/// One series in the bracket: the two teams, which of them holds home advantage, and how many games
/// it is played over.
/// </summary>
public sealed record PostseasonSeries(
    int Round,
    string? ConferenceName,
    PostseasonSeed Higher,
    PostseasonSeed Lower,
    int Length)
{
    /// <summary>Wins needed to take the series. A series length is odd, so this is a strict majority.</summary>
    public int GamesToWin => (Length / 2) + 1;

    public bool Involves(TeamId teamId) => Higher.TeamId == teamId || Lower.TeamId == teamId;
}

/// <summary>
/// Who reached the postseason, in bracket order, with everything the seeding rules had to say about
/// working it out.
/// </summary>
public sealed record PostseasonSeeding(
    IReadOnlyList<PostseasonSeed> Seeds,
    IReadOnlyList<string?> Groups,
    IReadOnlyList<RuleFinding> Warnings,
    IReadOnlyList<RuleFinding> Notes)
{
    public IReadOnlyList<PostseasonSeed> InGroup(string? groupName) =>
        Seeds.Where(seed => string.Equals(seed.ConferenceName, groupName, StringComparison.Ordinal)).ToArray();
}

/// <summary>
/// What the bracket wants scheduled on one day, and where the postseason has got to.
/// <para>
/// <see cref="Violations"/> is what a league whose postseason cannot be played reports — a bracket
/// needing more days than the calendar reserves for it, a ruleset stating a different number of
/// series lengths than the league has rounds. It is a violation rather than a warning because the
/// alternative is a season that stops halfway through its own postseason with nothing said.
/// </para>
/// </summary>
public sealed record PostseasonDraw(
    IReadOnlyList<Fixture> Fixtures,
    IReadOnlyList<RuleFinding> Violations,
    IReadOnlyList<RuleFinding> Notes,
    int LiveRound,
    bool IsComplete,
    TeamId? ChampionId);

/// <summary>
/// Draws the postseason: who qualifies, who plays whom, on which day, and where home advantage
/// sits.
/// <para>
/// Entirely a rule, and entirely deterministic — no seed, no clock, no randomness anywhere. The
/// bracket is a function of the table and the ruleset, which is what lets the same season replay to
/// the same champion. Sequencing it — noticing that a day has arrived and putting the fixtures into
/// the schedule — is <c>SeasonEngine</c>'s job, not this type's, the same division of labour the
/// schedule generator already keeps.
/// </para>
/// <para>
/// The bracket is drawn <b>a round at a time, and a game at a time</b> rather than laid out in full
/// at the start. It has to be: round two's participants are not known until round one is decided,
/// and a best-of-seven that ends in five never played its last two games. A schedule holding
/// fixtures that were never going to happen would make "games remaining" a lie and leave the season
/// permanently short of complete.
/// </para>
/// </summary>
public sealed class PostseasonBracketBuilder
{
    public const string NotConfiguredCode = "postseason.not_configured";
    public const string NoPostseasonPhaseCode = "postseason.calendar_reserves_no_days";
    public const string NeedsTwoConferencesCode = "postseason.bracket_needs_one_or_two_conferences";
    public const string TooFewTeamsCode = "postseason.too_few_teams_to_seed";
    public const string RoundCountMismatchCode = "postseason.round_count_mismatch";
    public const string RunsPastItsDaysCode = "postseason.runs_past_the_days_reserved_for_it";
    public const string SeededOnNoGamesCode = "postseason.seeded_before_any_games_were_played";
    public const string LastPlaceOnEqualRecordsCode = "postseason.last_place_taken_on_an_equal_record";

    /// <summary>
    /// Works out who qualifies, from the table as it stands. A flat league seeds from the league as a
    /// whole; an aligned league seeds each conference separately, which is what makes the last round
    /// a final between two conference winners rather than a fifth round of the same bracket.
    /// </summary>
    public DomainOperationResult<PostseasonSeeding> Seed(League league, Standings standings, PostseasonRules rules)
    {
        ArgumentNullException.ThrowIfNull(league);
        ArgumentNullException.ThrowIfNull(standings);
        ArgumentNullException.ThrowIfNull(rules);

        if (!rules.IsConfigured)
        {
            return DomainOperationResult<PostseasonSeeding>.Failure(new DomainError(
                NotConfiguredCode,
                "This league holds no postseason, so there is no bracket to seed."));
        }

        var alignment = league.Alignment;

        if (!alignment.IsFlat && alignment.Conferences.Count > 2)
        {
            return DomainOperationResult<PostseasonSeeding>.Failure(new DomainError(
                NeedsTwoConferencesCode,
                $"This league has {alignment.Conferences.Count} conferences. This build draws a bracket per conference and a final between two conference winners, so a league with more than two of them describes a format this build cannot play."));
        }

        var groups = alignment.IsFlat
            ? new List<string?> { null }
            : alignment.Conferences.Select(conference => (string?)conference.Name).ToList();

        var qualifiers = rules.QualifyingTeamsPerConference;
        var seeds = new List<PostseasonSeed>();
        var warnings = new List<RuleFinding>();
        var notes = new List<RuleFinding>();

        foreach (var group in groups)
        {
            var rows = group is null ? standings.Rows : standings.InConference(group);

            if (rows.Count < qualifiers)
            {
                return DomainOperationResult<PostseasonSeeding>.Failure(new DomainError(
                    TooFewTeamsCode,
                    $"{qualifiers} team(s) qualify from {group ?? "the league"}, which has {rows.Count}. A bracket cannot be drawn from fewer teams than it seeds."));
            }

            // The order the table is already in. Its tie-breaks — and every tie the league's stated
            // sequence failed to resolve — were settled once, by the standings calculator, and are
            // read off here rather than re-decided by a second ordering nobody stated.
            for (var index = 0; index < qualifiers; index++)
            {
                seeds.Add(new PostseasonSeed(
                    rows[index].TeamId,
                    group,
                    index + 1,
                    standings.PositionOf(rows[index].TeamId)));
            }

            // The place that decides who is in and who is out is the one worth naming. The table is
            // totally ordered whatever happens, so this never blocks a draw — it says that the last
            // ticket was not won on the floor.
            if (rows.Count > qualifiers && rows[qualifiers - 1].Overall == rows[qualifiers].Overall)
            {
                warnings.Add(new RuleFinding(
                    LastPlaceOnEqualRecordsCode,
                    $"{rows[qualifiers - 1].TeamName} takes the last postseason place in {group ?? "the league"} ahead of {rows[qualifiers].TeamName} on the same record ({rows[qualifiers - 1].Overall}). The order between them came from the table, and the table reports whether a stated tie-break settled it.",
                    rows[qualifiers - 1].TeamId));
            }
        }

        if (standings.Rows.All(row => row.GamesPlayed == 0))
        {
            warnings.Add(new RuleFinding(
                SeededOnNoGamesCode,
                "The bracket is being seeded from a table in which nobody has played a game, so every place in it was settled by the standings' terminal ordering key rather than by results."));
        }

        return DomainOperationResult<PostseasonSeeding>.Success(
            new PostseasonSeeding(seeds, groups, warnings, notes));
    }

    /// <summary>
    /// The fixtures the bracket wants played on <paramref name="day"/>, given everything played so
    /// far. Returns nothing — and nothing wrong — on a day outside the postseason, so the engine can
    /// ask on every day it advances through without first working out whether it needs to.
    /// </summary>
    public DomainOperationResult<PostseasonDraw> DrawFor(
        Season season,
        PostseasonSeeding seeding,
        PostseasonRules rules,
        LeagueCalendar calendar,
        SeasonSchedule schedule,
        IEnumerable<GameResult> results,
        SeasonDay day)
    {
        ArgumentNullException.ThrowIfNull(season);
        ArgumentNullException.ThrowIfNull(seeding);
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(calendar);
        ArgumentNullException.ThrowIfNull(schedule);
        ArgumentNullException.ThrowIfNull(results);
        ArgumentNullException.ThrowIfNull(day);

        var phase = calendar.Phase(SeasonPhase.Postseason);
        if (phase is null)
        {
            return DomainOperationResult<PostseasonDraw>.Failure(new DomainError(
                NoPostseasonPhaseCode,
                $"Season {season.Year}'s calendar reserves no postseason days, so no bracket can be played in it."));
        }

        var roundsPerGroup = rules.RoundsPerConference;
        var totalRounds = roundsPerGroup + (seeding.Groups.Count > 1 ? 1 : 0);

        if (rules.SeriesLengths.Count != totalRounds)
        {
            return DomainOperationResult<PostseasonDraw>.Success(new PostseasonDraw(
                [],
                [
                    new RuleFinding(
                        RoundCountMismatchCode,
                        $"This postseason has {totalRounds} round(s) but the ruleset states {rules.SeriesLengths.Count} series length(s), so at least one round has no length to be played over."),
                ],
                [],
                LiveRound: 0,
                IsComplete: false,
                ChampionId: null));
        }

        var postseasonResults = results.Where(result => result.Phase == SeasonPhase.Postseason).ToArray();
        var violations = new List<RuleFinding>();
        var notes = new List<RuleFinding>();

        var live = PairingsForFirstRound(seeding, rules);
        var roundStart = phase.StartDay;

        for (var round = 1; round <= totalRounds; round++)
        {
            var decided = live.Select(series => WinnerOf(series, postseasonResults)).ToArray();

            if (decided.All(winner => winner is not null))
            {
                if (round == totalRounds)
                {
                    return DomainOperationResult<PostseasonDraw>.Success(new PostseasonDraw(
                        [], violations, notes, round, IsComplete: true, ChampionId: decided[0]!.TeamId));
                }

                roundStart = roundStart.Plus(rules.SeriesLengthForRound(round));
                live = PairingsForNextRound(round + 1, decided.Select(winner => winner!).ToArray(), rules);
                continue;
            }

            var fixtures = DrawRound(season, live, decided, rules, roundStart, phase, schedule, day, violations);

            return DomainOperationResult<PostseasonDraw>.Success(new PostseasonDraw(
                fixtures, violations, notes, round, IsComplete: false, ChampionId: null));
        }

        // Unreachable: the loop above returns on the last round either way. Stated rather than left
        // to fall off the end, because a bracket that silently produced nothing would look exactly
        // like a bracket that had finished.
        return DomainOperationResult<PostseasonDraw>.Success(new PostseasonDraw(
            [], violations, notes, totalRounds, IsComplete: true, ChampionId: null));
    }

    /// <summary>
    /// One round's next game for every series still alive. Series run in lockstep — game <c>n</c> of
    /// every live series in a round falls on the same day — so no team is ever asked to play twice
    /// on one day, and a series that ends early simply stops asking for days.
    /// </summary>
    private static List<Fixture> DrawRound(
        Season season,
        IReadOnlyList<PostseasonSeries> series,
        IReadOnlyList<PostseasonSeed?> decided,
        PostseasonRules rules,
        SeasonDay roundStart,
        CalendarPhase phase,
        SeasonSchedule schedule,
        SeasonDay day,
        List<RuleFinding> violations)
    {
        var fixtures = new List<Fixture>();
        var alreadyOnDay = schedule.On(day);
        var slot = alreadyOnDay.Count;

        for (var index = 0; index < series.Count; index++)
        {
            if (decided[index] is not null)
            {
                continue;
            }

            var contest = series[index];
            var played = GamesPlayedIn(contest, schedule);
            var gameNumber = played + 1;
            var gameDay = roundStart.Plus(gameNumber - 1);

            if (!phase.Contains(gameDay))
            {
                violations.Add(new RuleFinding(
                    RunsPastItsDaysCode,
                    $"Game {gameNumber} of the round-{contest.Round} series between {contest.Higher.TeamId.Value} and {contest.Lower.TeamId.Value} falls on {gameDay}, and this league reserves {phase.LengthInDays} day(s) for its postseason. The bracket needs {rules.SeriesLengths.Sum()} day(s) to be played in full."));
                continue;
            }

            // A day the bracket has already been drawn for, or one it has not reached: both are
            // no-ops rather than errors. The engine asks on every day it passes through, and only
            // the day a series is actually due a game produces one.
            if (gameDay != day)
            {
                continue;
            }

            var higherHosts = rules.HomeCourtSequence.HigherSeedHosts(gameNumber);

            fixtures.Add(new Fixture(
                GameId.For(season, day, slot),
                day,
                higherHosts ? contest.Higher.TeamId : contest.Lower.TeamId,
                higherHosts ? contest.Lower.TeamId : contest.Higher.TeamId,
                SeasonPhase.Postseason));

            slot++;
        }

        return fixtures;
    }

    /// <summary>
    /// Games already scheduled in a series, counted from the schedule rather than from results.
    /// A fixture drawn for today but not yet played still occupies its day, and counting results
    /// would draw the same game again tomorrow.
    /// </summary>
    private static int GamesPlayedIn(PostseasonSeries series, SeasonSchedule schedule) =>
        schedule.Fixtures.Count(fixture =>
            fixture.Phase == SeasonPhase.Postseason &&
            fixture.Involves(series.Higher.TeamId) &&
            fixture.Involves(series.Lower.TeamId));

    /// <summary>
    /// Who took a series, or null while it is still alive. Two teams meet at most once in a
    /// single-elimination bracket — a rematch would mean both had come through the same series — so
    /// the unordered pair identifies the series without needing the round.
    /// </summary>
    private static PostseasonSeed? WinnerOf(PostseasonSeries series, IReadOnlyList<GameResult> results)
    {
        var higherWins = 0;
        var lowerWins = 0;

        foreach (var result in results)
        {
            if (!Involves(result, series.Higher.TeamId, series.Lower.TeamId))
            {
                continue;
            }

            if (result.WinnerId == series.Higher.TeamId)
            {
                higherWins++;
            }
            else
            {
                lowerWins++;
            }
        }

        if (higherWins >= series.GamesToWin)
        {
            return series.Higher;
        }

        return lowerWins >= series.GamesToWin ? series.Lower : null;
    }

    private static bool Involves(GameResult result, TeamId left, TeamId right) =>
        (result.HomeTeamId == left && result.AwayTeamId == right) ||
        (result.HomeTeamId == right && result.AwayTeamId == left);

    private static List<PostseasonSeries> PairingsForFirstRound(PostseasonSeeding seeding, PostseasonRules rules)
    {
        var length = rules.SeriesLengthForRound(1);
        var order = BracketOrder(rules.QualifyingTeamsPerConference);
        var series = new List<PostseasonSeries>();

        foreach (var group in seeding.Groups)
        {
            var seeds = seeding.InGroup(group);

            for (var index = 0; index < order.Count; index += 2)
            {
                series.Add(new PostseasonSeries(
                    1,
                    group,
                    seeds[order[index] - 1],
                    seeds[order[index + 1] - 1],
                    length));
            }
        }

        return series;
    }

    /// <summary>
    /// The next round, paired off the previous round's winners in the order they were drawn. Adjacent
    /// series meet, which is what makes the bracket a bracket: the top seed and the second seed of
    /// one conference are put in halves that cannot meet before the conference final.
    /// </summary>
    private static List<PostseasonSeries> PairingsForNextRound(
        int round,
        IReadOnlyList<PostseasonSeed> winners,
        PostseasonRules rules)
    {
        var length = rules.SeriesLengthForRound(round);
        var series = new List<PostseasonSeries>();

        for (var index = 0; index < winners.Count; index += 2)
        {
            var left = winners[index];
            var right = winners[index + 1];

            // The league-wide position, not the conference seed, because the last round is contested
            // between two conference winners and both of them are their conference's number one.
            var higher = left.LeaguePosition <= right.LeaguePosition ? left : right;
            var lower = ReferenceEquals(higher, left) ? right : left;

            series.Add(new PostseasonSeries(
                round,
                string.Equals(left.ConferenceName, right.ConferenceName, StringComparison.Ordinal) ? left.ConferenceName : null,
                higher,
                lower,
                length));
        }

        return series;
    }

    /// <summary>
    /// Seed numbers in bracket order, so that consecutive pairs are the first-round series: 1-8, 4-5,
    /// 2-7, 3-6 for eight qualifiers. Built by reflecting each round rather than written out, because
    /// the qualifier count is configured and any power of two has to draw correctly.
    /// </summary>
    private static IReadOnlyList<int> BracketOrder(int size)
    {
        var order = new List<int> { 1 };

        while (order.Count < size)
        {
            var doubled = new List<int>(order.Count * 2);
            var complement = (order.Count * 2) + 1;

            foreach (var seed in order)
            {
                doubled.Add(seed);
                doubled.Add(complement - seed);
            }

            order = doubled;
        }

        return order;
    }
}
