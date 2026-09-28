using System.Globalization;
using System.Text.Json;
using BallGM.Application.Leagues;
using BallGM.Infrastructure.AI;
using BallGM.Infrastructure.Cap;
using BallGM.Infrastructure.DraftAssets;
using BallGM.Infrastructure.LeaguePacks;
using BallGM.Infrastructure.Negotiations;
using BallGM.Infrastructure.Saves;
using BallGM.Infrastructure.Seasons;
using BallGM.Infrastructure.Trades;

// Usage: PackCheck <pack.json> [teams.csv] [seasons=20]
// Simulates the pack's opening season repeatedly under different seeds and compares the simulated
// table with the real one. Prints one JSON object, so tune.sh can read it.
if (args.Length < 1)
{
    Console.Error.WriteLine("usage: PackCheck <pack.json> [teams.csv] [seasons]");
    return 2;
}

var packPath = args[0];
var teamsCsv = args.Length > 1 && args[1] != "-" ? args[1] : null;
var seasons = args.Length > 2 ? int.Parse(args[2], CultureInfo.InvariantCulture) : 20;

using var packDocument = JsonDocument.Parse(File.ReadAllText(packPath));
var keyByName = packDocument.RootElement.GetProperty("teams").EnumerateArray()
    .ToDictionary(team => team.GetProperty("name").GetString()!, team => team.GetProperty("key").GetString()!);

var realWinPct = teamsCsv is null ? new Dictionary<string, double>() : ReadRecords(teamsCsv);
var simWins = keyByName.Values.ToDictionary(key => key, _ => new List<double>());
var seasonSds = new List<double>();
// Opt-in per-player output for rating calibration: summed simulated totals across every season run.
var playersOut = Environment.GetEnvironmentVariable("PACKCHECK_PLAYERS_OUT");
var playerTotals = new Dictionary<string, (string Name, int Games, int Minutes, int Points, int Rebounds, int Assists)>();
double points = 0, games = 0;

for (var season = 0; season < seasons; season++)
{
    var session = new LeagueSession(
        new LeaguePackDataSource(packPath),
        new RulesCapLedger(),
        new RulesDraftAssetLedger(),
        new RulesTradeEngine(),
        new RulesSigningEngine(),
        new RulesFreeAgencyMarket(),
        new RulesSeasonEngine(),
        new SaveGameSerializer(),
        new RulesFrontOfficeAdvisor(new RulesCapLedger()));

    var loaded = session.Load();
    Require(loaded.Errors);
    Require(session.StartSeason(seed: 1000 + season).Errors);
    Require(session.AdvanceToEndOfSeason().Errors);

    if (playersOut is not null)
    {
        var names = loaded.Value.Teams.SelectMany(team => team.Roster).ToDictionary(spot => spot.PlayerId, spot => spot.FullName);
        foreach (var (id, line) in session.PlayerSeasonTotals())
        {
            var name = names.GetValueOrDefault(id, id);
            playerTotals.TryGetValue(name, out var before);
            playerTotals[name] = (name, before.Games + line.GamesPlayed, before.Minutes + line.Minutes, before.Points + line.Points,
                before.Rebounds + line.Rebounds, before.Assists + line.Assists);
        }
    }

    var rows = session.Standings().Value.Rows;
    var pcts = rows.Select(row => row.Wins / (double)Math.Max(1, row.GamesPlayed)).ToList();
    seasonSds.Add(StdDev(pcts));
    foreach (var row in rows)
    {
        simWins[keyByName[row.TeamName]].Add(row.Wins / (double)Math.Max(1, row.GamesPlayed));
        points += row.PointsFor;
        games += row.GamesPlayed;
    }
}

var result = new Dictionary<string, object?>
{
    ["seasons"] = seasons,
    ["points_per_team_game"] = Math.Round(points / games, 2),
    ["sim_sd_win_pct"] = Math.Round(seasonSds.Average(), 4),
};

var paired = realWinPct.Keys.Where(simWins.ContainsKey).ToList();
if (paired.Count >= 3)
{
    var sim = paired.Select(key => simWins[key].Average()).ToList();
    var real = paired.Select(key => realWinPct[key]).ToList();
    result["real_sd_win_pct"] = Math.Round(StdDev(real), 4);
    result["r_sim_vs_real"] = Math.Round(Correlation(sim, real), 3);
    result["rmse_mean_win_pct"] = Math.Round(Math.Sqrt(sim.Zip(real, (s, r) => (s - r) * (s - r)).Average()), 4);
    result["teams"] = paired.OrderByDescending(key => realWinPct[key])
        .ToDictionary(key => key, key => new { real = Math.Round(realWinPct[key], 3), sim = Math.Round(simWins[key].Average(), 3) });
}

if (playersOut is not null)
{
    File.WriteAllLines(playersOut, new[] { "name,games,minutes,points,rebounds,assists" }.Concat(
        playerTotals.Values.Select(p => string.Join(",", $"\"{p.Name}\"", p.Games, p.Minutes, p.Points, p.Rebounds, p.Assists))));
}

Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
return 0;

static void Require(IReadOnlyList<BallGM.Domain.Common.DomainError> errors)
{
    if (errors.Count > 0)
    {
        throw new InvalidOperationException(string.Join("; ", errors.Select(error => $"{error.Code}: {error.Message}")));
    }
}

static Dictionary<string, double> ReadRecords(string path)
{
    var lines = File.ReadAllLines(path);
    var header = lines[0].Split(',');
    var keyColumn = Array.FindIndex(header, column => column is "TEAM_ABBREVIATION" or "key" or "Team" or "Tm");
    var winColumn = Array.IndexOf(header, "W");
    var lossColumn = Array.IndexOf(header, "L");
    return lines.Skip(1).Select(line => line.Split(','))
        .Where(cells => cells.Length > Math.Max(keyColumn, Math.Max(winColumn, lossColumn)))
        .ToDictionary(
            cells => cells[keyColumn].Trim(),
            cells =>
            {
                var wins = double.Parse(cells[winColumn], CultureInfo.InvariantCulture);
                var losses = double.Parse(cells[lossColumn], CultureInfo.InvariantCulture);
                return wins / (wins + losses);
            });
}

static double StdDev(IReadOnlyList<double> values)
{
    var mean = values.Average();
    return Math.Sqrt(values.Sum(value => (value - mean) * (value - mean)) / values.Count);
}

static double Correlation(IReadOnlyList<double> x, IReadOnlyList<double> y)
{
    var mx = x.Average();
    var my = y.Average();
    var cov = x.Zip(y, (a, b) => (a - mx) * (b - my)).Sum();
    return cov / Math.Sqrt(x.Sum(a => (a - mx) * (a - mx)) * y.Sum(b => (b - my) * (b - my)));
}
