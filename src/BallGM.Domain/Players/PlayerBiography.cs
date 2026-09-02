namespace BallGM.Domain.Players;

/// <summary>
/// Biographical facts about a player that never change once recorded: where they were born, the
/// programme they played before the league, and which draft — if any — brought them in. Every field
/// is optional because most of them are unknowable for a large share of a league's roster: a modder's
/// fixture content may not state a birthplace, and a player who signed as an undrafted free agent has
/// no draft record at all.
/// <para>
/// This is the seed `docs/competitive-feature-review.md` §2 names for relationship affinity —
/// "seeded from shared origin: birthplace, prior team, prior amateur programme, draft class" — a
/// Milestone 13 system this milestone only supplies the fields for, not the graph itself.
/// </para>
/// </summary>
public sealed record PlayerBiography(
    string? Birthplace,
    string? PriorProgramme,
    int? DraftSeasonYear,
    int? DraftRound,
    int? DraftSelection)
{
    /// <summary>A player with no recorded biography at all — the default for existing and undrafted players.</summary>
    public static PlayerBiography Unknown { get; } = new(null, null, null, null, null);

    public bool WasDrafted => DraftSeasonYear is not null;
}
