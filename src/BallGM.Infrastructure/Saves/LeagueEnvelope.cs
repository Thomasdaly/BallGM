namespace BallGM.Infrastructure.Saves;

/// <summary>One division: a name and the teams in it.</summary>
public sealed record LeagueDivisionEnvelope(string Name, IReadOnlyList<string> TeamIds);

/// <summary>One conference: a name and the divisions inside it.</summary>
public sealed record LeagueConferenceEnvelope(string Name, IReadOnlyList<LeagueDivisionEnvelope> Divisions);

/// <summary>One team's line in a concluded season's archived table.</summary>
public sealed record SeasonHistoryTeamRecordEnvelope(
    string TeamId,
    int Position,
    int Wins,
    int Losses,
    int PointsFor,
    int PointsAgainst);

/// <summary>
/// One concluded season, archived. <see cref="ChampionTeamId"/> is absent for a league that held no
/// postseason — there was no champion to name, not a zero.
/// </summary>
public sealed record SeasonHistoryEntryEnvelope(
    int SeasonYear,
    string? ChampionTeamId,
    IReadOnlyList<SeasonHistoryTeamRecordEnvelope> FinalStandings);

/// <summary>
/// Serialization shape for the <c>League</c> aggregate: identity, how its teams are grouped, and
/// every season it has concluded. <see cref="Conferences"/> is empty for a flat league — the same
/// convention <c>LeagueAlignment.Flat</c> uses — rather than a separate flag, so a flat league and
/// one whose alignment happened to produce no groups read identically on both sides of a save.
/// <para>
/// Team membership itself is not here: it is redundant with <see cref="TeamEnvelope"/>, and
/// <c>League.Create</c> takes team identifiers directly from the teams a save also carries, so there
/// is one list to keep in step rather than two.
/// </para>
/// </summary>
public sealed record LeagueEnvelope(
    string LeagueId,
    string Name,
    IReadOnlyList<LeagueConferenceEnvelope> Conferences,
    IReadOnlyList<SeasonHistoryEntryEnvelope> History);
