namespace BallGM.Application.Players;

/// <summary>
/// A team's payroll across the coming seasons: every player's salary season by season (the grid a
/// GM plans from), and per season what is committed, what rides on options, and the room left
/// against the league's lines — projected forward at an assumed rate of cap growth.
/// </summary>
public sealed record CapOutlookSummary(
    string TeamId,
    string TeamName,
    int CapGrowthPercent,
    IReadOnlyList<string> Seasons,
    IReadOnlyList<CapOutlookPlayerRow> Players,
    IReadOnlyList<CapOutlookSeason> Totals);

/// <summary>One player's row of the grid: a cell per season, empty once he is off the books.</summary>
public sealed record CapOutlookPlayerRow(string PlayerId, string FullName, string Position, int Overall, IReadOnlyList<CapOutlookCell> Cells);

/// <summary>One season of one player: salary, whether it is an option year (and whose), and whether it comes from an extension.</summary>
public sealed record CapOutlookCell(long? Salary, string? Option, bool IsExtension);

/// <summary>
/// One season's totals. <see cref="Guaranteed"/> is payroll as the cap counts it (options not yet
/// taken up are not counted); <see cref="Options"/> is what pending options would add;
/// <see cref="RosterHolds"/> charges for spots still to fill. Lines are projected at the outlook's
/// growth rate; <see cref="CapRoom"/> is negative when over.
/// </summary>
public sealed record CapOutlookSeason(
    string Season,
    int SeasonYear,
    int PlayersUnderContract,
    long Guaranteed,
    long Options,
    long RosterHolds,
    long TotalPayroll,
    long? SoftCap,
    long? CapRoom,
    long? LuxuryTax,
    long? RoomUnderTax,
    long? FirstApron,
    long? SecondApron,
    string Standing);

/// <summary>Players whose contracts end, grouped by the summer they reach free agency.</summary>
public sealed record FreeAgentClass(int Year, string Summer, IReadOnlyList<UpcomingFreeAgentLine> Players);

public sealed record UpcomingFreeAgentLine(
    string PlayerId,
    string FullName,
    string TeamId,
    string TeamName,
    string Position,
    int Overall,
    int Age,
    long CurrentSalary,
    int FreeAgentYear,
    string? FinalSeasonOption,
    bool ExtensionEligible,
    bool RefusedExtension,
    string? PortraitPath);

/// <summary>What an extension to this player would have to look like, and what he wants.</summary>
public sealed record ExtensionTermsSummary(
    string PlayerId,
    string FullName,
    bool Eligible,
    string Reason,
    int StartSeason,
    int MaximumSeasons,
    long? MinimumSalary,
    long? MaximumSalary,
    long? AskingPrice,
    long CurrentSalary);

/// <summary>The answer to an extension offer: legal or not, accepted or refused, and why.</summary>
public sealed record ExtensionOutcomeSummary(
    bool IsLegal,
    bool Accepted,
    string Code,
    string Explanation,
    long? AskingPrice,
    IReadOnlyList<string> Violations);

/// <summary>A player who has turned down his team's extension offer this season — an alert until he re-signs or the season ends.</summary>
public sealed record ExtensionRefusalLine(
    string PlayerId,
    string FullName,
    string TeamId,
    string TeamName,
    long Offered,
    long? AskingPrice,
    int Season,
    string Explanation);
