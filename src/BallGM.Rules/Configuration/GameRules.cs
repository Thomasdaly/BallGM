using BallGM.Domain.Common;

namespace BallGM.Rules.Configuration;

/// <summary>
/// The rules of a game itself that a league may set: how many personal fouls put a player out of the
/// game, and how many team fouls a period — and, separately, an overtime period, which is far shorter —
/// it takes before every further defensive foul sends the other side to the line (the bonus). A mode
/// with a documented default rather than an optional limit: every league plays with some foul limit,
/// so absence means <see cref="Default"/>, the sport's usual six, four and three.
/// </summary>
public sealed record GameRules
{
    /// <summary>The most a league may set either figure to: past this the rule has stopped meaning anything.</summary>
    public const int MaximumStatedFouls = 20;

    private const string InvalidFoulLimitCode = "ruleset.invalid_personal_foul_limit";
    private const string InvalidBonusCode = "ruleset.invalid_bonus_team_fouls";

    private GameRules(int personalFoulLimit, int bonusTeamFoulsPerPeriod, int bonusTeamFoulsPerOvertime)
    {
        PersonalFoulLimit = personalFoulLimit;
        BonusTeamFoulsPerPeriod = bonusTeamFoulsPerPeriod;
        BonusTeamFoulsPerOvertime = bonusTeamFoulsPerOvertime;
    }

    /// <summary>Six personal fouls and out; the bonus from the fifth team foul of a period, the fourth of an overtime.</summary>
    public static GameRules Default { get; } = new(6, 4, 3);

    /// <summary>The personal foul that puts a player out of the game: at this many, he plays no further part.</summary>
    public int PersonalFoulLimit { get; }

    /// <summary>Team fouls a period allowed before each further defensive foul is two free throws (the bonus).</summary>
    public int BonusTeamFoulsPerPeriod { get; }

    /// <summary>The same for an overtime period, which is short enough to need its own, lower figure.</summary>
    public int BonusTeamFoulsPerOvertime { get; }

    public static DomainOperationResult<GameRules> Create(int? personalFoulLimit, int? bonusTeamFoulsPerPeriod, int? bonusTeamFoulsPerOvertime)
    {
        var limit = personalFoulLimit ?? Default.PersonalFoulLimit;
        var bonus = bonusTeamFoulsPerPeriod ?? Default.BonusTeamFoulsPerPeriod;
        var overtimeBonus = bonusTeamFoulsPerOvertime ?? Default.BonusTeamFoulsPerOvertime;

        if (limit < 1 || limit > MaximumStatedFouls)
        {
            return DomainOperationResult<GameRules>.Failure(new DomainError(
                InvalidFoulLimitCode,
                $"A personal foul limit of {limit} is not a rule: it must be between 1 and {MaximumStatedFouls}."));
        }

        foreach (var stated in new[] { bonus, overtimeBonus })
        {
            if (stated < 0 || stated > MaximumStatedFouls)
            {
                return DomainOperationResult<GameRules>.Failure(new DomainError(
                    InvalidBonusCode,
                    $"A bonus after {stated} team fouls is not a rule: it must be between 0 and {MaximumStatedFouls}."));
            }
        }

        return DomainOperationResult<GameRules>.Success(new GameRules(limit, bonus, overtimeBonus));
    }
}
