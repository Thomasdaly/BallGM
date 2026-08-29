namespace BallGM.Domain.Seasons;

/// <summary>
/// What a league is doing on a given day. The phases are ordered as a season runs through them, and
/// a calendar is nothing but these four laid end to end across a day index.
/// </summary>
public enum SeasonPhase
{
    Preseason = 1,
    RegularSeason = 2,
    Postseason = 3,
    Offseason = 4,
}
