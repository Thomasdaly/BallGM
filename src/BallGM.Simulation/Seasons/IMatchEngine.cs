using BallGM.Domain.Common;
using BallGM.Domain.Seasons;

namespace BallGM.Simulation.Seasons;

/// <summary>
/// One game's worth of everything the engine needs: the fixture, both rotations, and the seed this
/// game — and only this game — is played with.
/// </summary>
public sealed record MatchSetup(
    Fixture Fixture,
    DepthChart HomeRotation,
    DepthChart AwayRotation,
    int Seed);

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

    DomainOperationResult<GameResult> Play(MatchSetup setup);
}

/// <summary>
/// The match engine of a build that has a calendar but not yet a model for deciding games.
/// <para>
/// It plays nothing and says so. A season advanced through it passes its days, keeps its fixtures,
/// and builds a table from whatever results were recorded from outside — which is exactly what a
/// standings test wants, and exactly what the first half of this milestone ships.
/// </para>
/// </summary>
public sealed class UnplayedMatchEngine : IMatchEngine
{
    public const string NoMatchEngineCode = "season.no_match_model_in_this_build";

    public bool CanPlay => false;

    public DomainOperationResult<GameResult> Play(MatchSetup setup)
    {
        ArgumentNullException.ThrowIfNull(setup);

        return DomainOperationResult<GameResult>.Failure(new DomainError(
            NoMatchEngineCode,
            $"Game '{setup.Fixture.Id.Value}' was reached, but this build has no model for deciding a game, so it was left unplayed."));
    }
}
