using System.Globalization;
using BallGM.Application.Players;

namespace BallGM.Client.Avalonia.ViewModels;

/// <summary>One attribute on the sheet: its name, its value (always printed), and what it covers.</summary>
public sealed record AttributeCell(string Name, int Value, string Description)
{
    public string AccessibleName => $"{Name} {Value}";
}

/// <summary>A titled group of attributes, as a scouting sheet blocks them.</summary>
public sealed record AttributeGroupView(string Title, IReadOnlyList<AttributeCell> Attributes);

/// <summary>How at home a player is at one position; the familiarity is printed, the shade only repeats it.</summary>
public sealed record PositionChip(string Position, string Familiarity, int Level)
{
    public bool IsNatural => Level == 4;

    public bool IsAccomplished => Level == 3;

    public bool IsCompetent => Level == 2;

    public string AccessibleName => $"{Position}: {Familiarity.ToLowerInvariant()}";
}

/// <summary>
/// The scouting sheet as the profile shows it: the attributes in three columns (scoring and playmaking;
/// defence and rebounding; mental and physical), and the report above them — ability as a coach's stars,
/// personality, measurements, positions and traits. Built from the read model; computes nothing.
/// </summary>
public sealed class AttributeSheetView
{
    /// <summary>Groups per column: the sheet's groups two at a time, in the order the read model sends them.</summary>
    private const int GroupsPerColumn = 2;

    public AttributeSheetView(AttributeSheet sheet, int currentAbility)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        Columns = sheet.Groups
            .Select(group => new AttributeGroupView(
                group.Group,
                group.Attributes.Select(line => new AttributeCell(AttributeDescriptions.NameOf(line.Key), line.Value, AttributeDescriptions.For(line.Key))).ToList()))
            .Chunk(GroupsPerColumn)
            .Select(column => (IReadOnlyList<AttributeGroupView>)column)
            .ToList();
        CurrentStars = Stars(AbilityStars.HalfStars(currentAbility));
        PotentialStars = Stars(AbilityStars.HalfStars(sheet.PotentialAbility));
        CurrentStarsName = $"Current ability: {StarCount(AbilityStars.HalfStars(currentAbility))} of 5 stars";
        PotentialStarsName = $"Potential ability: {StarCount(AbilityStars.HalfStars(sheet.PotentialAbility))} of 5 stars";
        Personality = sheet.Personality;
        Body = sheet.Body is { } body ? Measurements(body) : null;
        Traits = sheet.Traits.Select(DisplayText.Words).ToList();
        Positions = sheet.Positions.Select(line => new PositionChip(line.Position, line.Familiarity, line.Level)).ToList();
    }

    public IReadOnlyList<IReadOnlyList<AttributeGroupView>> Columns { get; }

    public string CurrentStars { get; }

    public string PotentialStars { get; }

    public string CurrentStarsName { get; }

    public string PotentialStarsName { get; }

    public string Personality { get; }

    /// <summary>"203 cm (6 ft 8 in) · wingspan 213 cm · 104 kg (229 lb)", or null when the sheet has no measurements.</summary>
    public string? Body { get; }

    public bool HasBody => Body is not null;

    public IReadOnlyList<string> Traits { get; }

    public bool HasTraits => Traits.Count > 0;

    public IReadOnlyList<PositionChip> Positions { get; }

    /// <summary>Five stars, filled to the half-star count: "★★★★½" style, empty stars as outlines.</summary>
    internal static string Stars(int halfStars)
    {
        var full = halfStars / 2;
        var half = halfStars % 2 == 1;
        return new string('★', full) + (half ? "½" : string.Empty) + new string('☆', 5 - full - (half ? 1 : 0));
    }

    private static string StarCount(int halfStars) => (halfStars / 2.0).ToString("0.#", CultureInfo.InvariantCulture);

    private static string Measurements(BodyLine body)
    {
        var inches = (int)Math.Round(body.HeightCm / 2.54, MidpointRounding.AwayFromZero);
        var pounds = (int)Math.Round(body.WeightKg * 2.20462, MidpointRounding.AwayFromZero);
        return string.Create(CultureInfo.InvariantCulture, $"{body.HeightCm} cm ({inches / 12} ft {inches % 12} in) · wingspan {body.WingspanCm} cm · {body.WeightKg} kg ({pounds} lb)");
    }
}
