using System.Collections.Frozen;
using System.Text.Json;
using BallGM.Domain.Players;
using BallGM.Domain.Randomness;
using BallGM.Rules.Players;

namespace BallGM.Infrastructure.Players;

/// <summary>Measurements as content states them: centimetres and kilograms, any of them missing in a malformed file.</summary>
public sealed record PlayerBodyEnvelope(int? HeightCm, int? WingspanCm, int? WeightKg);

/// <summary>
/// The one on-disk shape of a player's sheet, shared by league packs and saves so the two cannot
/// drift: visible and hidden attributes as camelCase-keyed maps, potential, body, positions by name
/// and traits by name. Reading never throws on content — it returns the reason it refused.
/// </summary>
public static class PlayerSheetCodec
{
    private static readonly string[] LegacyKeys = ["height", "speed", "strength", "passing", "lateralQuickness"];

    private static readonly string[] VisibleKeys = PlayerAttributes.All.Select(attribute => CamelCase(attribute.ToString())).ToArray();

    private static readonly string[] HiddenKeys = PlayerAttributes.AllHidden.Select(attribute => CamelCase(attribute.ToString())).ToArray();

    private static readonly FrozenSet<string> VisibleKeySet = VisibleKeys.ToFrozenSet(StringComparer.Ordinal);

    private static readonly FrozenSet<string> HiddenKeySet = HiddenKeys.ToFrozenSet(StringComparer.Ordinal);

    public static string Key(PlayerAttribute attribute) => VisibleKeys[(int)attribute];

    public static string Key(HiddenAttribute attribute) => HiddenKeys[(int)attribute];

    public static Dictionary<string, int?> VisibleMap(PlayerRating rating) =>
        PlayerAttributes.All.ToDictionary(Key, attribute => (int?)rating[attribute]);

    public static Dictionary<string, int?> HiddenMap(PlayerRating rating) =>
        PlayerAttributes.AllHidden.ToDictionary(Key, attribute => (int?)rating.Hidden(attribute));

    public static Dictionary<string, string> PositionMap(PlayerRating rating) =>
        rating.Positions.OrderBy(entry => entry.Key).ToDictionary(entry => entry.Key.ToString(), entry => entry.Value.ToString());

    public static List<string> TraitList(PlayerRating rating) => rating.Traits.Select(trait => trait.ToString()).ToList();

    public static PlayerBodyEnvelope? BodyOf(PlayerRating rating) =>
        rating.Body is { } body ? new PlayerBodyEnvelope(body.HeightCm, body.WingspanCm, body.WeightKg) : null;

    /// <summary>
    /// A full sheet from the five attributes packs stated before schema 3 (height, speed, strength,
    /// passing, lateral quickness), expanded deterministically from <paramref name="identity"/> — the
    /// player's name, birth date and position, so two players who share a name do not share a sheet.
    /// Refused if anything of the full sheet is stated alongside them.
    /// </summary>
    public static PlayerRating? ReadLegacy(IReadOnlyDictionary<string, int?>? ratings, bool statesMoreThanRatings, Position position, int age, string identity, out string? error)
    {
        if (ratings is null || statesMoreThanRatings || ratings.Count != LegacyKeys.Length || !LegacyKeys.All(ratings.ContainsKey))
        {
            error = "A schema 1 or 2 pack states exactly five ratings (height, speed, strength, passing, lateralQuickness) and nothing of the full sheet; the full sheet needs schema 3.";
            return null;
        }

        var values = LegacyKeys.Select(key => ratings[key]).ToArray();
        if (values.Any(value => value is not int rating || !InRange(rating)))
        {
            error = $"All five ratings ({string.Join(", ", LegacyKeys)}) must be stated, each between {PlayerRating.MinimumOverall} and {PlayerRating.MaximumOverall}.";
            return null;
        }

        error = null;
        return RatingProfileGenerator.FromLegacy(values[0]!.Value, values[1]!.Value, values[2]!.Value, values[3]!.Value, values[4]!.Value, position, age, SeedMixer.Mix(0, identity));
    }

    /// <summary>
    /// A full sheet as stated. Every visible attribute is required; hidden attributes default to the
    /// middle, potential to Current Ability, an absent position map to <paramref name="natural"/>
    /// alone, traits to none.
    /// </summary>
    public static PlayerRating? Read(
        IReadOnlyDictionary<string, int?>? ratings,
        IReadOnlyDictionary<string, int?>? hidden,
        int? potential,
        PlayerBodyEnvelope? body,
        IReadOnlyDictionary<string, string>? positions,
        IReadOnlyList<string>? traits,
        Position natural,
        out string? error)
    {
        error = null;
        if (ratings is null)
        {
            error = "Ratings are required.";
            return null;
        }

        if (ratings.Keys.FirstOrDefault(key => !VisibleKeySet.Contains(key)) is { } unknown)
        {
            error = $"Ratings name an attribute this build does not know: {unknown}.";
            return null;
        }

        var visible = new int[PlayerAttributes.Count];
        for (var index = 0; index < visible.Length; index++)
        {
            if (ratings.GetValueOrDefault(VisibleKeys[index]) is not int value || !InRange(value))
            {
                error = $"Rating '{VisibleKeys[index]}' must be stated, between {PlayerRating.MinimumOverall} and {PlayerRating.MaximumOverall}.";
                return null;
            }

            visible[index] = value;
        }

        if (hidden?.Keys.FirstOrDefault(key => !HiddenKeySet.Contains(key)) is { } strayHidden)
        {
            error = $"Hidden attribute '{strayHidden}' is not one this build knows.";
            return null;
        }

        var hiddenValues = new int[PlayerAttributes.HiddenCount];
        for (var index = 0; index < hiddenValues.Length; index++)
        {
            var value = hidden?.GetValueOrDefault(HiddenKeys[index]) ?? PlayerRating.NeutralHidden;
            if (!InRange(value))
            {
                error = $"Hidden attribute '{HiddenKeys[index]}' must be between {PlayerRating.MinimumOverall} and {PlayerRating.MaximumOverall}.";
                return null;
            }

            hiddenValues[index] = value;
        }

        PlayerBody? measured = null;
        if (body is not null)
        {
            if (body.HeightCm is not int height || body.WingspanCm is not int wingspan || body.WeightKg is not int weight || !PlayerBody.IsValid(height, wingspan, weight))
            {
                error = $"A body states height ({PlayerBody.MinimumHeightCm}–{PlayerBody.MaximumHeightCm} cm), wingspan (within {PlayerBody.MaximumWingspanDifferenceCm} cm of height) and weight ({PlayerBody.MinimumWeightKg}–{PlayerBody.MaximumWeightKg} kg).";
                return null;
            }

            measured = new PlayerBody(height, wingspan, weight);
        }

        // Absent: his natural position alone. Stated, even empty: exactly as stated, so a save keeps an unassessed sheet unassessed.
        var familiarity = positions is null
            ? new Dictionary<Position, PositionFamiliarity> { [natural] = PositionFamiliarity.Natural }
            : [];
        foreach (var (positionName, familiarityName) in positions ?? new Dictionary<string, string>())
        {
            if (!TryName<Position>(positionName, out var position) || !TryName<PositionFamiliarity>(familiarityName, out var level))
            {
                error = $"Position familiarity '{positionName}: {familiarityName}' is not a position and one of {string.Join(", ", Enum.GetNames<PositionFamiliarity>())}.";
                return null;
            }

            familiarity[position] = level;
        }

        var traitValues = new List<PlayerTrait>();
        foreach (var traitName in traits ?? [])
        {
            if (!TryName<PlayerTrait>(traitName, out var trait))
            {
                error = $"Trait '{traitName}' is not one of {string.Join(", ", Enum.GetNames<PlayerTrait>())}.";
                return null;
            }

            traitValues.Add(trait);
        }

        var potentialValue = potential ?? PlayerAttributes.CurrentAbility(visible);
        if (!InRange(potentialValue))
        {
            error = $"Potential must be between {PlayerRating.MinimumOverall} and {PlayerRating.MaximumOverall}.";
            return null;
        }

        return new PlayerRating(visible, hiddenValues, potentialValue, measured, familiarity, traitValues);
    }

    private static string CamelCase(string name) => JsonNamingPolicy.CamelCase.ConvertName(name);

    /// <summary>An enum value by its exact name only — not a number, not a comma list, as <c>Enum.TryParse</c> would accept.</summary>
    private static bool TryName<T>(string name, out T value)
        where T : struct, Enum
    {
        value = default;
        return Names<T>.All.Contains(name) && Enum.TryParse(name, out value);
    }

    private static bool InRange(int value) => value is >= PlayerRating.MinimumOverall and <= PlayerRating.MaximumOverall;

    private static class Names<T>
        where T : struct, Enum
    {
        public static readonly FrozenSet<string> All = Enum.GetNames<T>().ToFrozenSet(StringComparer.Ordinal);
    }
}
