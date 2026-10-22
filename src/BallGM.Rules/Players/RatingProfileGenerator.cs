using BallGM.Domain.Players;
using BallGM.Domain.Randomness;

namespace BallGM.Rules.Players;

/// <summary>
/// Turns one already-decided "how good is this player" number into a full scouting sheet: every
/// visible attribute, the hidden ones, Potential Ability, body measurements, position familiarity and
/// traits. The shared generator behind draft classes, the shipped league's rosters and the generated
/// calibration league, so the structure the sim audit measures is real everywhere a player comes from.
/// <para>
/// A latent-factor model, not independent noise: a player's <em>position</em> sets an archetype (a
/// centre's interior game, a point guard's handle), six <em>factors</em> — shooting, athleticism,
/// playmaking, defence, motor and feel — are drawn once and each loads onto its own attributes, and
/// per-attribute <em>noise</em> finishes it. Shooters shoot from everywhere; athletes are quick, explosive
/// and finish; a high-motor big rebounds and boxes out. The sheet is then shifted so its Current
/// Ability is the talent asked for. Every attribute clamps to the caller's bounds, so a ruleset's
/// stated rating band keeps meaning something.
/// </para>
/// </summary>
public static class RatingProfileGenerator
{
    /// <summary>
    /// Factor magnitude, in points either side of zero, triangular. Tuned so the first principal
    /// component of the visible attributes carries a share of the variance inside sim-audit row 11's
    /// band (0.20–0.70) over a league-like talent spread — talent alone is shared by every attribute,
    /// so without real factors and archetypes the first component swallows everything. Pinned by
    /// <c>RatingProfileGeneratorTests</c>; re-measure before changing it.
    /// </summary>
    public const int DefaultSpread = 14;

    /// <summary>Per-attribute noise, in points either side of zero.</summary>
    public const int AttributeNoise = 6;

    /// <summary>Per-tendency noise around what the sheet implies: two players with one game still choose a little differently.</summary>
    public const int TendencyNoise = 8;

    private enum Factor
    {
        Shooting,
        Athleticism,
        Playmaking,
        Defence,
        Motor,
        Feel,
    }

    private static readonly int FactorCount = Enum.GetValues<Factor>().Length;

    public static PlayerRating Generate(int talent, Position position, int age, int lowerBound, int upperBound, IRandomSource random) =>
        Generate(talent, position, age, lowerBound, upperBound, DefaultSpread, random);

    public static PlayerRating Generate(int talent, Position position, int age, int lowerBound, int upperBound, int spread, IRandomSource random)
    {
        ArgumentNullException.ThrowIfNull(random);

        if (lowerBound > upperBound)
        {
            throw new ArgumentException($"Lower bound ({lowerBound}) cannot exceed upper bound ({upperBound}).", nameof(lowerBound));
        }

        if (spread < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(spread), spread, "Spread cannot be negative.");
        }

        var factors = new int[FactorCount];
        for (var factor = 0; factor < FactorCount; factor++)
        {
            factors[factor] = Triangular(spread, random);
        }

        var raw = new int[PlayerAttributes.Count];
        foreach (var attribute in PlayerAttributes.All)
        {
            var (factor, loading) = Loading(attribute);
            raw[(int)attribute] = talent + Archetype(attribute, position) + (factors[(int)factor] * loading / 100) + Triangular(spread == 0 ? 0 : AttributeNoise, random);
        }

        // Shift the whole sheet so its Current Ability is the talent asked for, then clamp.
        var shift = talent - PlayerAttributes.CurrentAbility(raw);
        var visible = raw.Select(value => Math.Clamp(value + shift, lowerBound, upperBound)).ToArray();
        var currentAbility = PlayerAttributes.CurrentAbility(visible);

        var hidden = PlayerAttributes.AllHidden.Select(_ => Math.Clamp(PlayerRating.NeutralHidden + Triangular(35, random), 1, 100)).ToArray();
        var body = Body(position, factors[(int)Factor.Athleticism], random);
        var potential = Math.Clamp(currentAbility + Headroom(age, hidden[(int)HiddenAttribute.Professionalism], random), currentAbility, PlayerRating.MaximumOverall);
        var positions = Familiarity(position, hidden[(int)HiddenAttribute.Versatility]);
        var traits = Traits(visible, currentAbility, random);

        var sheet = new PlayerRating(visible, hidden, potential, body, positions, traits);
        var tendencies = TendencyModel.Derive(sheet)
            .Select(value => Math.Clamp(value + Triangular(TendencyNoise, random), TendencyModel.Minimum, TendencyModel.Maximum))
            .ToArray();
        return sheet.WithTendencies(tendencies);
    }

    /// <summary>
    /// A full sheet from the five attributes an older pack states (height, speed, strength, passing and
    /// lateral quickness, 0–100): generated around their mean as talent, then the five pinned where
    /// they map — speed and first step to speed, lateral quickness to itself, strength to itself, the
    /// handle, passing and vision to passing — and height turned into centimetres from how far it sits
    /// above or below the other four (the old engine read size as height less the mean, so a tall
    /// star and a short one are told apart by that gap, not by how good they are). Deterministic in
    /// <paramref name="seed"/>, which the caller derives from the player, so a pack loads the same way
    /// every time.
    /// </summary>
    public static PlayerRating FromLegacy(int height, int speed, int strength, int passing, int lateralQuickness, Position position, int age, int seed)
    {
        var talent = (height + speed + strength + passing + lateralQuickness) / 5;
        var generated = Generate(talent, position, age, PlayerRating.MinimumOverall, PlayerRating.MaximumOverall, new SeededRandomSource(seed));
        var visible = generated.Visible.ToArray();
        visible[(int)PlayerAttribute.Speed] = speed;
        visible[(int)PlayerAttribute.FirstStep] = speed;
        visible[(int)PlayerAttribute.LateralQuickness] = lateralQuickness;
        visible[(int)PlayerAttribute.Strength] = strength;
        visible[(int)PlayerAttribute.Passing] = passing;
        visible[(int)PlayerAttribute.Vision] = (passing + visible[(int)PlayerAttribute.Vision]) / 2;
        visible[(int)PlayerAttribute.BallHandling] = (passing + visible[(int)PlayerAttribute.BallHandling]) / 2;

        // Same height shape as before: the old height's gap from the player's level, now in centimetres.
        var heightCm = Math.Clamp(PlayerBody.HeightCmForShape(height - talent), PlayerBody.MinimumHeightCm, PlayerBody.MaximumHeightCm);
        var body = generated.Body is { } drawn
            ? new PlayerBody(heightCm, Math.Clamp(heightCm + (drawn.WingspanCm - drawn.HeightCm), heightCm - PlayerBody.MaximumWingspanDifferenceCm, heightCm + PlayerBody.MaximumWingspanDifferenceCm), drawn.WeightKg)
            : null;
        var headroom = generated.PotentialAbility - generated.Overall;
        var potential = Math.Min(PlayerRating.MaximumOverall, PlayerAttributes.CurrentAbility(visible) + headroom);
        var expanded = new PlayerRating(visible, generated.HiddenValues, potential, body, generated.Positions, generated.Traits);
        return expanded.WithTendencies(TendencyModel.Derive(expanded));
    }

    /// <summary>Which factor an attribute loads on, and how strongly (percent of the factor's draw).</summary>
    private static (Factor Factor, int Loading) Loading(PlayerAttribute attribute) => attribute switch
    {
        PlayerAttribute.MidRange or PlayerAttribute.ThreePoint or PlayerAttribute.FreeThrow => (Factor.Shooting, 100),
        PlayerAttribute.CloseRange or PlayerAttribute.ShotCreation => (Factor.Shooting, 60),
        PlayerAttribute.Finishing or PlayerAttribute.Dunking => (Factor.Athleticism, 70),
        PlayerAttribute.Speed or PlayerAttribute.FirstStep or PlayerAttribute.Vertical => (Factor.Athleticism, 100),
        PlayerAttribute.LateralQuickness => (Factor.Athleticism, 80),
        PlayerAttribute.BallHandling or PlayerAttribute.Passing or PlayerAttribute.Vision => (Factor.Playmaking, 100),
        PlayerAttribute.OffBallMovement => (Factor.Feel, 70),
        PlayerAttribute.PostScoring or PlayerAttribute.Strength => (Factor.Motor, 60),
        PlayerAttribute.PerimeterDefence or PlayerAttribute.InteriorDefence or PlayerAttribute.HelpDefence => (Factor.Defence, 100),
        PlayerAttribute.Stealing or PlayerAttribute.Blocking or PlayerAttribute.DefensiveDiscipline => (Factor.Defence, 70),
        PlayerAttribute.OffensiveRebounding or PlayerAttribute.DefensiveRebounding or PlayerAttribute.BoxingOut => (Factor.Motor, 100),
        PlayerAttribute.ScreenSetting or PlayerAttribute.WorkRate or PlayerAttribute.Hustle or PlayerAttribute.Stamina => (Factor.Motor, 80),
        PlayerAttribute.NaturalFitness or PlayerAttribute.Aggression => (Factor.Motor, 50),
        _ => (Factor.Feel, 100),
    };

    /// <summary>
    /// Where a position sits against the talent line on each attribute, in points: the centre's size
    /// game up, his handle and range down; the point guard's the other way. Weighted to roughly zero
    /// across the sheet, so a position does not change Current Ability — the shift makes it exact.
    /// </summary>
    private static int Archetype(PlayerAttribute attribute, Position position)
    {
        var slot = Slot(position);
        return attribute switch
        {
            PlayerAttribute.Finishing => Row(0, 2, 3, 2, 4, slot),
            PlayerAttribute.Dunking => Row(-14, -5, 2, 8, 14, slot),
            PlayerAttribute.PostScoring => Row(-16, -9, -2, 8, 16, slot),
            PlayerAttribute.CloseRange => Row(4, 2, 0, -2, -2, slot),
            PlayerAttribute.MidRange => Row(4, 5, 2, -2, -8, slot),
            PlayerAttribute.ThreePoint => Row(6, 8, 3, -5, -16, slot),
            PlayerAttribute.FreeThrow => Row(6, 6, 2, -3, -10, slot),
            PlayerAttribute.ShotCreation => Row(10, 7, 2, -7, -14, slot),
            PlayerAttribute.BallHandling => Row(16, 8, 0, -9, -18, slot),
            PlayerAttribute.Passing => Row(16, 4, 0, -5, -9, slot),
            PlayerAttribute.Vision => Row(14, 4, 0, -5, -9, slot),
            PlayerAttribute.OffBallMovement => Row(0, 6, 4, 0, -3, slot),
            PlayerAttribute.ScreenSetting or PlayerAttribute.InteriorDefence or PlayerAttribute.OffensiveRebounding => Row(-16, -9, -2, 9, 18, slot),
            PlayerAttribute.PerimeterDefence => Row(4, 6, 4, -4, -14, slot),
            PlayerAttribute.HelpDefence => Row(-3, 0, 2, 3, 5, slot),
            PlayerAttribute.Stealing => Row(8, 5, 2, -3, -8, slot),
            PlayerAttribute.Blocking => Row(-18, -11, -2, 9, 20, slot),
            PlayerAttribute.DefensiveRebounding => Row(-14, -9, -2, 9, 18, slot),
            PlayerAttribute.BoxingOut => Row(-14, -9, -2, 9, 16, slot),
            PlayerAttribute.BasketballIq => Row(4, 0, 0, 0, 0, slot),
            PlayerAttribute.Speed or PlayerAttribute.FirstStep => Row(12, 9, 2, -7, -16, slot),
            PlayerAttribute.LateralQuickness => Row(10, 7, 2, -7, -14, slot),
            PlayerAttribute.Strength => Row(-12, -7, 0, 9, 16, slot),
            PlayerAttribute.Vertical => Row(0, 2, 4, 4, 2, slot),
            PlayerAttribute.Stamina => Row(3, 3, 2, 0, -5, slot),
            _ => 0,
        };
    }

    /// <summary>A position's place in a five-wide table: point guard first, centre last.</summary>
    private static int Slot(Position position) => (int)position - 1;

    private static int Row(int pointGuard, int shootingGuard, int smallForward, int powerForward, int centre, int slot) => slot switch
    {
        0 => pointGuard,
        1 => shootingGuard,
        2 => smallForward,
        3 => powerForward,
        _ => centre,
    };

    private static PlayerBody Body(Position position, int athleticism, IRandomSource random)
    {
        var slot = Slot(position);
        var meanHeight = Row(188, 196, 201, 206, 211, slot);
        var height = meanHeight + Triangular(7, random);
        var wingspan = height + 4 + Triangular(7, random);
        var weight = Row(88, 95, 102, 108, 116, slot) + ((height - meanHeight) * 9 / 10) - (athleticism / 4) + Triangular(7, random);
        return new PlayerBody(
            Math.Clamp(height, PlayerBody.MinimumHeightCm, PlayerBody.MaximumHeightCm),
            Math.Clamp(wingspan, height - PlayerBody.MaximumWingspanDifferenceCm, height + PlayerBody.MaximumWingspanDifferenceCm),
            Math.Clamp(weight, PlayerBody.MinimumWeightKg, PlayerBody.MaximumWeightKg));
    }

    /// <summary>How far above today a player can still grow: wide at nineteen, nothing past twenty-six; professionalism adds a little.</summary>
    private static int Headroom(int age, int professionalism, IRandomSource random)
    {
        var room = age switch
        {
            <= 19 => 30,
            20 => 25,
            21 => 20,
            22 => 15,
            23 => 10,
            24 => 6,
            25 or 26 => 3,
            _ => 0,
        };

        return room == 0 ? 0 : random.NextInt32(0, room + 1) + ((professionalism - PlayerRating.NeutralHidden) * room / 200);
    }

    private static Dictionary<Position, PositionFamiliarity> Familiarity(Position natural, int versatility)
    {
        var familiarity = new Dictionary<Position, PositionFamiliarity>();
        foreach (var position in Enum.GetValues<Position>())
        {
            familiarity[position] = Math.Abs(position - natural) switch
            {
                0 => PositionFamiliarity.Natural,
                1 => versatility >= 60 ? PositionFamiliarity.Accomplished : PositionFamiliarity.Competent,
                2 => versatility >= 75 ? PositionFamiliarity.Competent : PositionFamiliarity.Unconvincing,
                _ => versatility >= 85 ? PositionFamiliarity.Unconvincing : PositionFamiliarity.Awkward,
            };
        }

        return familiarity;
    }

    /// <summary>Habits that follow from a standout skill, each taken up by most but not all who have it.</summary>
    private static List<PlayerTrait> Traits(int[] visible, int currentAbility, IRandomSource random)
    {
        int Above(PlayerAttribute attribute) => visible[(int)attribute] - currentAbility;
        (PlayerTrait Trait, bool Qualifies)[] candidates =
        [
            (PlayerTrait.StretchesTheFloor, Above(PlayerAttribute.ThreePoint) >= 10),
            (PlayerTrait.DrivesToTheBasket, Above(PlayerAttribute.FirstStep) >= 8 && Above(PlayerAttribute.Finishing) >= 5),
            (PlayerTrait.PostsUp, Above(PlayerAttribute.PostScoring) >= 8),
            (PlayerTrait.PassFirst, Above(PlayerAttribute.Vision) >= 10 && Above(PlayerAttribute.Passing) >= 8),
            (PlayerTrait.ShootsOffTheDribble, Above(PlayerAttribute.ShotCreation) >= 8),
            (PlayerTrait.GamblesForSteals, Above(PlayerAttribute.Stealing) >= 10 && Above(PlayerAttribute.DefensiveDiscipline) <= 0),
            (PlayerTrait.CrashesTheOffensiveGlass, Above(PlayerAttribute.OffensiveRebounding) >= 10),
            (PlayerTrait.TakesCharges, Above(PlayerAttribute.DefensiveDiscipline) >= 8 && Above(PlayerAttribute.Strength) >= 0),
            (PlayerTrait.AvoidsContact, Above(PlayerAttribute.Strength) <= -12),
            (PlayerTrait.RunsTheFloor, Above(PlayerAttribute.Speed) >= 10),
        ];

        // One draw per candidate whatever it qualifies for, so the draw sequence does not depend on the sheet.
        return candidates
            .Where(candidate => random.NextInt32(0, 100) < 60 && candidate.Qualifies)
            .Select(candidate => candidate.Trait)
            .ToList();
    }

    /// <summary>A triangular draw over ±<paramref name="spread"/>: the mean of two uniforms, so the middle is likelier than the edges.</summary>
    private static int Triangular(int spread, IRandomSource random) =>
        spread == 0 ? 0 : (random.NextInt32(-spread, spread + 1) + random.NextInt32(-spread, spread + 1)) / 2;
}
