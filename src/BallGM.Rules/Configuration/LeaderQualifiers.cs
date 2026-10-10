using BallGM.Domain.Common;

namespace BallGM.Rules.Configuration;

/// <summary>
/// Who qualifies for the league's statistical leaderboards: a share of the team's games played, and for
/// the shooting percentages a number of makes per team game, so a 3-for-3 cameo never leads the league;
/// for the per-36 boards a number of minutes a game, so a three-minute specialist never leads them.
/// A mode with a documented default rather than an optional limit: every league ranks its leaders
/// somehow, so absence means <see cref="Default"/> — 70% of games; 15 minutes a game; 3.66 field goals,
/// 1.00 three and 1.52 free throws made per team game (in hundredths, so integer).
/// </summary>
public sealed record LeaderQualifiers
{
    private const string InvalidShareCode = "ruleset.invalid_leader_games_share";
    private const string InvalidMakesCode = "ruleset.invalid_leader_makes";
    private const string InvalidMinutesCode = "ruleset.invalid_leader_minutes";

    private LeaderQualifiers(int minimumGamesShare, int minutes, int fieldGoals, int threes, int freeThrows)
    {
        MinimumGamesShareBasisPoints = minimumGamesShare;
        MinimumMinutesPerGame = minutes;
        MinimumFieldGoalsMadePerTeamGame = fieldGoals;
        MinimumThreesMadePerTeamGame = threes;
        MinimumFreeThrowsMadePerTeamGame = freeThrows;
    }

    public static LeaderQualifiers Default { get; } = new(7_000, 15, 366, 100, 152);

    /// <summary>The share of his team's games a player must have played, in basis points.</summary>
    public int MinimumGamesShareBasisPoints { get; }

    /// <summary>Minutes a game, on top of the games share, to rank on a per-36 board.</summary>
    public int MinimumMinutesPerGame { get; }

    /// <summary>Field goals made per team game, in hundredths, to rank on FG% (and eFG%, TS%).</summary>
    public int MinimumFieldGoalsMadePerTeamGame { get; }

    /// <summary>Threes made per team game, in hundredths, to rank on 3P%.</summary>
    public int MinimumThreesMadePerTeamGame { get; }

    /// <summary>Free throws made per team game, in hundredths, to rank on FT%.</summary>
    public int MinimumFreeThrowsMadePerTeamGame { get; }

    public static DomainOperationResult<LeaderQualifiers> Create(int? minimumGamesShare, int? minutes, int? fieldGoals, int? threes, int? freeThrows)
    {
        var share = minimumGamesShare ?? Default.MinimumGamesShareBasisPoints;
        if (share is < 0 or > 10_000)
        {
            return DomainOperationResult<LeaderQualifiers>.Failure(new DomainError(
                InvalidShareCode,
                $"A leaderboard games share of {share} basis points is not a share: it must be between 0 and 10,000."));
        }

        var minutesPerGame = minutes ?? Default.MinimumMinutesPerGame;
        if (minutesPerGame is < 0 or > 48)
        {
            return DomainOperationResult<LeaderQualifiers>.Failure(new DomainError(
                InvalidMinutesCode,
                $"A per-36 qualifier of {minutesPerGame} minutes a game is not possible: it must be between 0 and 48."));
        }

        int[] makes = [fieldGoals ?? Default.MinimumFieldGoalsMadePerTeamGame, threes ?? Default.MinimumThreesMadePerTeamGame, freeThrows ?? Default.MinimumFreeThrowsMadePerTeamGame];
        if (makes.Any(made => made is < 0 or > 10_000))
        {
            return DomainOperationResult<LeaderQualifiers>.Failure(new DomainError(
                InvalidMakesCode,
                "A leaderboard's makes per team game must be between 0 and 100.00 (stated in hundredths)."));
        }

        return DomainOperationResult<LeaderQualifiers>.Success(new LeaderQualifiers(share, minutesPerGame, makes[0], makes[1], makes[2]));
    }
}
