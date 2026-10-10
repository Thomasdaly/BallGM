namespace BallGM.Domain.Players;

/// <summary>The six groups a player's visible attributes are read in, as a scouting sheet lays them out.</summary>
public enum AttributeGroup
{
    Scoring,
    Playmaking,
    Defence,
    Rebounding,
    Mental,
    Physical,
}

/// <summary>
/// A visible attribute, 0–100. Declared in sheet order, group by group, so the enum's order is the
/// order every screen and every save lists them in; <see cref="PlayerAttributes.GroupOf"/> says which
/// group each belongs to. Adding one means a save and pack schema bump.
/// </summary>
public enum PlayerAttribute
{
    // Scoring
    Finishing,
    Dunking,
    PostScoring,
    CloseRange,
    MidRange,
    ThreePoint,
    FreeThrow,
    ShotCreation,

    // Playmaking
    BallHandling,
    Passing,
    Vision,
    OffBallMovement,
    ScreenSetting,

    // Defence
    PerimeterDefence,
    InteriorDefence,
    HelpDefence,
    Stealing,
    Blocking,
    DefensiveDiscipline,

    // Rebounding
    OffensiveRebounding,
    DefensiveRebounding,
    BoxingOut,

    // Mental
    BasketballIq,
    Anticipation,
    Composure,
    Concentration,
    WorkRate,
    Hustle,
    Aggression,
    Leadership,
    Teamwork,

    // Physical
    Speed,
    FirstStep,
    LateralQuickness,
    Strength,
    Vertical,
    Stamina,
    NaturalFitness,
}

/// <summary>
/// An attribute a player carries but a screen never prints: what scouting reports describe in words
/// and what development, form and availability read. 0–100, 50 the league's middle.
/// </summary>
public enum HiddenAttribute
{
    Consistency,
    BigGames,
    InjuryProneness,
    Versatility,
    Professionalism,
    Ambition,
    Temperament,
    Loyalty,
    Adaptability,
}

/// <summary>How at home a player is at a position, best last, so a higher value is a better fit.</summary>
public enum PositionFamiliarity
{
    Awkward,
    Unconvincing,
    Competent,
    Accomplished,
    Natural,
}

/// <summary>A player's habit on the floor: a tendency the match engine leans on, not a skill level.</summary>
public enum PlayerTrait
{
    DrivesToTheBasket,
    StretchesTheFloor,
    PostsUp,
    PassFirst,
    ShootsOffTheDribble,
    GamblesForSteals,
    CrashesTheOffensiveGlass,
    TakesCharges,
    AvoidsContact,
    RunsTheFloor,
}

/// <summary>The fixed facts about the attribute set: groups, and the weights Current Ability reads them with.</summary>
public static class PlayerAttributes
{
    public static IReadOnlyList<PlayerAttribute> All { get; } = Enum.GetValues<PlayerAttribute>();

    public static IReadOnlyList<HiddenAttribute> AllHidden { get; } = Enum.GetValues<HiddenAttribute>();

    public static int Count => All.Count;

    public static int HiddenCount => AllHidden.Count;

    public static AttributeGroup GroupOf(PlayerAttribute attribute) => attribute switch
    {
        <= PlayerAttribute.ShotCreation => AttributeGroup.Scoring,
        <= PlayerAttribute.ScreenSetting => AttributeGroup.Playmaking,
        <= PlayerAttribute.DefensiveDiscipline => AttributeGroup.Defence,
        <= PlayerAttribute.BoxingOut => AttributeGroup.Rebounding,
        <= PlayerAttribute.Teamwork => AttributeGroup.Mental,
        _ => AttributeGroup.Physical,
    };

    /// <summary>
    /// How much each attribute counts towards Current Ability, out of 100 in all, in sheet order: the
    /// skills that win possessions most (creating and making shots, defending, decisions, athleticism)
    /// count most. Position-blind on purpose — a position's own rating weights them its own way — and
    /// fixed here rather than in a ruleset, so Current Ability means the same thing in every pack.
    /// </summary>
    private static ReadOnlySpan<byte> Weights =>
    [
        4, 1, 2, 2, 3, 5, 2, 5, // scoring
        4, 4, 3, 2, 1, // playmaking
        4, 4, 3, 2, 2, 1, // defence
        2, 3, 1, // rebounding
        5, 3, 3, 2, 2, 1, 1, 1, 2, // mental
        3, 3, 3, 3, 3, 3, 2, // physical
    ];

    public static int AbilityWeight(PlayerAttribute attribute) => Weights[(int)attribute];

    /// <summary>Current Ability of a sheet's visible attributes, in sheet order: their weighted mean.</summary>
    public static int CurrentAbility(ReadOnlySpan<int> visible)
    {
        var weights = Weights;
        var total = 0;
        for (var index = 0; index < weights.Length; index++)
        {
            total += weights[index] * visible[index];
        }

        return total / 100;
    }
}
