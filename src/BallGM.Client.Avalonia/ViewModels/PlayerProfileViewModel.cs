using System.Globalization;
using BallGM.Application.Players;

namespace BallGM.Client.Avalonia.ViewModels;

/// <summary>
/// The player profile: identity, the five attributes as bars, contract, recent form, and career —
/// the screen a management sim opens when a name is clicked. Averages and percentages are display
/// choices made here; everything counted comes from the session's profile query.
/// </summary>
public sealed class PlayerProfileViewModel(PlayerProfileSummary profile, Action back) : ViewModelBase
{
    public RelayCommand BackCommand { get; } = new(back);

    public string FullName => profile.FullName;

    public string Position => profile.Position;

    public int Overall => profile.Overall;

    public string? PortraitPath => profile.PortraitPath;

    public string TeamLine => profile.TeamName ?? "Free agent";

    public string BioLine => string.Create(
        CultureInfo.InvariantCulture,
        $"Age {profile.Age} · born {profile.BirthDate:d MMM yyyy} · {profile.SeasonsOfService} season{(profile.SeasonsOfService == 1 ? string.Empty : "s")} of service");

    public bool IsInjured => profile.IsInjured;

    public string? InjuryDescription => profile.InjuryDescription;

    public IReadOnlyList<AttributeRow> Attributes { get; } =
    [
        new("Height", profile.Attributes.Height, "Size: rebounding, rim protection, finishing over defenders."),
        new("Speed", profile.Attributes.Speed, "Getting up the floor and past a defender."),
        new("Strength", profile.Attributes.Strength, "Holding position, finishing through contact."),
        new("Passing", profile.Attributes.Passing, "Creating shots for team-mates."),
        new("Lateral quickness", profile.Attributes.LateralQuickness, "Staying in front on defence."),
    ];

    public IReadOnlyList<ContractRow> Contract { get; } = profile.Contract
        .Select(line => new ContractRow(
            LeagueSeasonLabel(line.Season),
            ViewModels.MoneyDisplay.ToMillions(line.Salary),
            line.IsOption ? "Option" : string.Empty))
        .ToList();

    public bool HasContract => profile.Contract.Count > 0;

    public string ContractSummary => profile.Contract.Count == 0
        ? "Not under contract."
        : $"{profile.Contract.Count} season{(profile.Contract.Count == 1 ? string.Empty : "s")} remaining · {ViewModels.MoneyDisplay.ToMillions(profile.Contract.Sum(line => line.Salary))} total";

    public bool HasSeason => profile.CurrentSeason is not null;

    public StatTile[] SeasonTiles { get; } = profile.CurrentSeason is { } season
        ?
        [
            new("GP", season.GamesPlayed.ToString(CultureInfo.InvariantCulture)),
            new("MPG", PerGame(season.Minutes, season.GamesPlayed)),
            new("PPG", PerGame(season.Points, season.GamesPlayed)),
            new("RPG", PerGame(season.Rebounds, season.GamesPlayed)),
            new("APG", PerGame(season.Assists, season.GamesPlayed)),
        ]
        : [];

    public bool HasRecentGames => profile.RecentGames.Count > 0;

    public IReadOnlyList<RecentGameRow> RecentGames { get; } = profile.RecentGames.Select(RecentGameRow.From).ToList();

    public bool HasCareer => profile.Career.Count > 0;

    public IReadOnlyList<CareerRow> Career { get; } = profile.Career.Select(CareerRow.From).ToList();

    public bool HasSimulatedStats => profile.SeasonDetail is not null;

    public IReadOnlyList<StatGroup> SimulatedGroups { get; } = profile.SeasonDetail is { } detail
        ? PlayerStatsSections.SimulatedSeason(detail)
        : [];

    /// <summary>The latest stated (not simulated) season that carries metrics, e.g. last real season.</summary>
    private static CareerSeasonLine? LastStated(PlayerProfileSummary profile) =>
        profile.Career.LastOrDefault(season => !season.IsCurrent && season.Metrics is { Count: > 0 });

    public bool HasStatedStats => LastStated(profile) is not null;

    public string StatedSeasonTitle => LastStated(profile) is { } season ? $"{season.Season} — AS STATED BY THE DATA PACK" : string.Empty;

    public IReadOnlyList<StatGroup> StatedGroups { get; } = LastStated(profile) is { } stated
        ? PlayerStatsSections.StatedSeason(stated)
        : [];

    // Contract, in depth.
    public bool HasContractDetail => profile.ContractDetail is { Years.Count: > 0 };

    public IReadOnlyList<StatTile> ContractTiles { get; } = profile.ContractDetail is { Years.Count: > 0 } c
        ?
        [
            new("Total value", ViewModels.MoneyDisplay.ToMillions(c.TotalValue)),
            new("Guaranteed", ViewModels.MoneyDisplay.ToMillions(c.GuaranteedValue)),
            new("Average per season", ViewModels.MoneyDisplay.ToMillions(c.AverageAnnualValue)),
            new("Free agent in", c.FreeAgentYear is { } year ? LeagueSeasonLabel(year) : "–"),
        ]
        : [];

    public IReadOnlyList<ContractYearRow> ContractYears { get; } = profile.ContractDetail?.Years
        .Select(ContractYearRow.From).ToList() ?? [];

    /// <summary>Where this season's salary sits between the player's minimum and maximum, 0..1.</summary>
    public double SalaryScalePosition => profile.ContractDetail is { MaximumSalary: { } max, MinimumSalary: { } min, Years.Count: > 0 } c && max > min
        ? Math.Clamp((double)(c.Years[0].Salary - min) / (max - min), 0, 1)
        : 0;

    public double SalaryScaleFill => 360 * SalaryScalePosition;

    public string SalaryScaleLine => profile.ContractDetail switch
    {
        { MaximumSalary: { } max, MinimumSalary: { } min, MaximumPercentOfCap: { } percent, Years.Count: > 0 } c =>
            string.Create(CultureInfo.InvariantCulture, $"Minimum for {profile.SeasonsOfService} seasons of service {ViewModels.MoneyDisplay.ToMillions(min)} · maximum {ViewModels.MoneyDisplay.ToMillions(max)} ({percent}% of the cap) · this season is {100.0 * c.Years[0].Salary / max:0}% of his maximum."),
        _ => "This league states no minimum or maximum salary scale.",
    };

    public string CareerTotalsLine => profile.Career.Count == 0
        ? string.Empty
        : CareerRow.TotalsLine(profile.Career);

    internal static string PerGame(int total, int games) =>
        games == 0 ? "–" : ((double)total / games).ToString("0.0", CultureInfo.InvariantCulture);

    private static string LeagueSeasonLabel(int startYear) =>
        string.Create(CultureInfo.InvariantCulture, $"{startYear}-{(startYear + 1) % 100:00}");
}

public sealed record AttributeRow(string Name, int Value, string Meaning);

public sealed record ContractRow(string Season, string Salary, string Note);

public sealed record StatTile(string Label, string Value);

public sealed record RecentGameRow(string Date, string Opponent, string Result, bool Won, string Minutes, string Points, string Rebounds, string Assists, string Shooting)
{
    public static RecentGameRow From(RecentGameLine game) => new(
        game.Date,
        (game.IsHome ? "vs " : "@ ") + game.Opponent,
        string.Create(CultureInfo.InvariantCulture, $"{(game.Won ? "W" : "L")} {game.TeamPoints}–{game.OpponentPoints}"),
        game.Won,
        game.Minutes.ToString(CultureInfo.InvariantCulture),
        game.Points.ToString(CultureInfo.InvariantCulture),
        game.Rebounds.ToString(CultureInfo.InvariantCulture),
        game.Assists.ToString(CultureInfo.InvariantCulture),
        string.Create(CultureInfo.InvariantCulture, $"{game.FieldGoalsMade}-{game.FieldGoalsAttempted}"));
}

public sealed record ContractYearRow(string Season, string Salary, string Guaranteed, string Option, string ShareOfCap)
{
    public static ContractYearRow From(ContractYearLine year) => new(
        string.Create(CultureInfo.InvariantCulture, $"{year.Season}-{(year.Season + 1) % 100:00}"),
        ViewModels.MoneyDisplay.ToMillions(year.Salary),
        year.Guaranteed == year.Salary ? "Fully" : ViewModels.MoneyDisplay.ToMillions(year.Guaranteed),
        year.Option is null ? "–" : $"{year.Option} option",
        year.ShareOfCap is { } share ? (share * 100).ToString("0.0", CultureInfo.InvariantCulture) + "%" : "–");
}

public sealed record CareerRow(
    string Season, string Team, string Games, string Minutes, string Points, string Rebounds, string Assists, bool IsCurrent,
    string FieldGoalPct, string ThreePct, string TrueShooting, string Usage, string NetRating, string Pie)
{
    public static CareerRow From(CareerSeasonLine line)
    {
        var m = line.Metrics ?? new Dictionary<string, double>();
        string Pct(double? value) => value is null ? "–" : (value.Value * 100).ToString("0.0", CultureInfo.InvariantCulture);
        double? Ratio(string made, string attempted) =>
            m.TryGetValue(made, out var numerator) && m.TryGetValue(attempted, out var denominator) && denominator > 0 ? numerator / denominator : null;
        string Metric(string code, Func<double, string> format) => m.TryGetValue(code, out var value) ? format(value) : "–";

        return new CareerRow(
            line.Season,
            line.TeamName ?? "–",
            line.GamesPlayed.ToString(CultureInfo.InvariantCulture),
            PlayerProfileViewModel.PerGame(line.Minutes, line.GamesPlayed),
            PlayerProfileViewModel.PerGame(line.Points, line.GamesPlayed),
            PlayerProfileViewModel.PerGame(line.Rebounds, line.GamesPlayed),
            PlayerProfileViewModel.PerGame(line.Assists, line.GamesPlayed),
            line.IsCurrent,
            Pct(Ratio("FGM", "FGA")),
            Pct(Ratio("FG3M", "FG3A")),
            Metric("TS_PCT", value => Pct(value)),
            Metric("USG_PCT", value => Pct(value)),
            Metric("NET_RATING", value => value.ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture)),
            Metric("PIE", value => Pct(value)));
    }

    public static string TotalsLine(IReadOnlyList<CareerSeasonLine> career)
    {
        var games = career.Sum(line => line.GamesPlayed);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"Career: {career.Count} season{(career.Count == 1 ? string.Empty : "s")} · {games} games · {PlayerProfileViewModel.PerGame(career.Sum(line => line.Points), games)} PPG · {PlayerProfileViewModel.PerGame(career.Sum(line => line.Rebounds), games)} RPG · {PlayerProfileViewModel.PerGame(career.Sum(line => line.Assists), games)} APG");
    }
}
