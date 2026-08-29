using BallGM.Domain.Common;
using BallGM.Domain.Players;
using BallGM.Domain.Seasons;
using BallGM.Domain.Teams;
using BallGM.Rules.Seasons;

namespace BallGM.Simulation.Seasons;

/// <summary>
/// One side of a game as the match engine needs to see it: who is in the rotation and for how long,
/// how good they are, and how long since they last played.
/// <para>
/// The rotation and the ratings travel separately because <see cref="DepthChart"/> deliberately does
/// not carry a rating — it is the answer to "who plays and for how long", and the minutes on it have
/// already been clamped into the bounds a rotation runs inside. Recovering strength from minutes is
/// therefore impossible by construction: a team of journeymen and a team of stars both allocate the
/// same 240 minutes. So the engine is handed both.
/// </para>
/// </summary>
/// <param name="RestDays">
/// Days since this team's previous game. 1 is the second night of a back-to-back. A team with no
/// previous game is fully rested, and the sequencing layer says so rather than the model guessing.
/// </param>
public sealed record MatchTeam(
    TeamId TeamId,
    DepthChart Rotation,
    IReadOnlyList<AvailablePlayer> Available,
    int RestDays)
{
    /// <summary>The rating of one player in this rotation, or null for somebody not in it.</summary>
    public int? OverallOf(PlayerId playerId)
    {
        ArgumentNullException.ThrowIfNull(playerId);

        return Available.FirstOrDefault(player => player.PlayerId == playerId)?.Overall;
    }
}

/// <summary>One game's worth of everything the engine needs, and the seed this game — and only this game — is played with.</summary>
public sealed record MatchSetup(Fixture Fixture, MatchTeam Home, MatchTeam Away, int Seed);

/// <summary>
/// Somebody hurt in a game. Counted in days rather than in season days, because the model has no
/// calendar: it knows a knock costs a fortnight, and the sequencing layer knows which fortnight.
/// </summary>
public sealed record MatchInjury
{
    public MatchInjury(PlayerId playerId, TeamId teamId, string description, int daysOut)
    {
        ArgumentNullException.ThrowIfNull(playerId);
        ArgumentNullException.ThrowIfNull(teamId);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);

        if (daysOut < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(daysOut), daysOut, "An injury has to cost at least one day.");
        }

        PlayerId = playerId;
        TeamId = teamId;
        Description = description;
        DaysOut = daysOut;
    }

    public PlayerId PlayerId { get; }

    public TeamId TeamId { get; }

    public string Description { get; }

    public int DaysOut { get; }
}

/// <summary>
/// What happened in one game: the result and its box score, and anybody who got hurt playing it.
/// <para>
/// Injuries come back beside the result rather than being applied by the model, because an injury is
/// a fact about a <em>season</em> — it has a start day and an end day — and the match engine has no
/// calendar. <c>SeasonEngine</c> turns each one into an <see cref="InjurySpell"/> against the day the
/// game was played on. Same division of labour as everywhere else: the model decides what happened,
/// the sequencing layer decides when.
/// </para>
/// </summary>
public sealed record MatchOutcome(GameResult Result, IReadOnlyList<MatchInjury> Injuries)
{
    public static MatchOutcome Unhurt(GameResult result) => new(result, []);
}

/// <summary>
/// Plays one game.
/// <para>
/// The seam between the season's bookkeeping — a calendar, a fixture list, a table — and the
/// probabilistic model that decides who wins. Splitting them means the calendar can be advanced,
/// tested, and proved deterministic without a single probability being involved, and it is why
/// Milestone 7 splits into a half with no randomness in it and a half that is nothing but.
/// </para>
/// <para>
/// <see cref="CanPlay"/> exists so that a build with no match model is a stated condition rather
/// than a crash: the season still advances, the days still pass, and every fixture it walked past
/// unplayed is reported as a note.
/// </para>
/// </summary>
public interface IMatchEngine
{
    /// <summary>Whether this build can decide a game at all.</summary>
    bool CanPlay { get; }

    DomainOperationResult<MatchOutcome> Play(MatchSetup setup);
}

/// <summary>
/// The match engine of a build that has a calendar but not yet a model for deciding games.
/// <para>
/// It plays nothing and says so. A season advanced through it passes its days, keeps its fixtures,
/// and builds a table from whatever results were recorded from outside — which is exactly what a
/// standings test wants, and exactly what the first half of this milestone shipped.
/// </para>
/// </summary>
public sealed class UnplayedMatchEngine : IMatchEngine
{
    public const string NoMatchEngineCode = "season.no_match_model_in_this_build";

    public bool CanPlay => false;

    public DomainOperationResult<MatchOutcome> Play(MatchSetup setup)
    {
        ArgumentNullException.ThrowIfNull(setup);

        return DomainOperationResult<MatchOutcome>.Failure(new DomainError(
            NoMatchEngineCode,
            $"Game '{setup.Fixture.Id.Value}' was reached, but this build has no model for deciding a game, so it was left unplayed."));
    }
}
