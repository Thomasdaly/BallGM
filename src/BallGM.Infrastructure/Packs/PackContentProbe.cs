using System.Diagnostics;
using System.Globalization;
using BallGM.Application.Leagues;
using BallGM.Application.Packs;
using BallGM.Domain.Common;
using BallGM.Infrastructure.AI;
using BallGM.Infrastructure.Cap;
using BallGM.Infrastructure.DraftAssets;
using BallGM.Infrastructure.Fixtures;
using BallGM.Infrastructure.LeaguePacks;
using BallGM.Infrastructure.Negotiations;
using BallGM.Infrastructure.Rulesets;
using BallGM.Infrastructure.Saves;
using BallGM.Infrastructure.Seasons;
using BallGM.Infrastructure.Trades;
using BallGM.Rules.Configuration;

namespace BallGM.Infrastructure.Packs;

/// <summary>
/// The concrete <see cref="IPackContentProbe"/>: reads rulesets with the ruleset serializer, leagues
/// with the league-pack loader, and plays seasons through a real <see cref="LeagueSession"/> wired
/// exactly as the client wires one — so "the checker says it plays" and "it plays" are the same code.
/// </summary>
public sealed class PackContentProbe : IPackContentProbe
{
    /// <summary>How many offending names a fit finding lists before it says "and N more".</summary>
    private const int ExampleCount = 3;

    private readonly LeagueRulesetSerializer _serializer = new();

    public PackProbeResult<RulesetSummary> ProbeRuleset(string rulesetJson)
    {
        var ruleset = Read(rulesetJson, out var findings);
        return ruleset is null
            ? new PackProbeResult<RulesetSummary>(null, findings)
            : new PackProbeResult<RulesetSummary>(
                new RulesetSummary(ruleset.Name, ruleset.RosterLimits.MinimumPlayers, ruleset.RosterLimits.MaximumPlayers, ruleset.RegularSeasonGameCount),
                findings);
    }

    public PackProbeResult<LeagueSnapshot> ProbeLeague(LeagueSource source, string rulesetJson)
    {
        ArgumentNullException.ThrowIfNull(source);
        var ruleset = Read(rulesetJson, out var rulesetFindings);
        if (ruleset is null)
        {
            return new PackProbeResult<LeagueSnapshot>(null, rulesetFindings);
        }

        var loaded = Load(source, ruleset);
        if (loaded.IsFailure)
        {
            return new PackProbeResult<LeagueSnapshot>(
                null,
                loaded.Errors
                    .Select(error => PackFinding.Error(error.Code, PackCheckStage.League, error.Message))
                    .ToList());
        }

        var findings = Fit(loaded.Value, ruleset);
        if (source is LeaguePackSource)
        {
            var derived = loaded.Value.Players.Count(player =>
                loaded.Value.Artwork.PlayerKeys.TryGetValue(player.Id.Value, out var key) &&
                (key == LeaguePackKeys.Derive(player.FullName, player.BirthDate) || key.StartsWith(LeaguePackKeys.Derive(player.FullName, player.BirthDate) + "-", StringComparison.Ordinal)));
            if (derived > 0)
            {
                findings.Add(PackFinding.Info(
                    "league.derived_player_keys",
                    PackCheckStage.League,
                    $"{ExplanationText.Count(derived, "player")} {Verb(derived, "states", "state")} no key, so face packs find them by a key made from name and birth date (like '{LeaguePackKeys.Derive("Aldo Brenner", new DateOnly(2001, 3, 14))}'). Such a key changes if the league pack renames the player; a stated 'key' never does."));
            }
        }

        return new PackProbeResult<LeagueSnapshot>(loaded.Value, findings);
    }

    public IReadOnlyList<PackFinding> SmokeTestSeason(LeagueSource source, string rulesetJson, int seed)
    {
        ArgumentNullException.ThrowIfNull(source);
        var ruleset = Read(rulesetJson, out var findings);
        if (ruleset is null)
        {
            return findings;
        }

        var loaded = Load(source, ruleset);
        if (loaded.IsFailure)
        {
            return loaded.Errors.Select(error => PackFinding.Error(error.Code, PackCheckStage.League, error.Message)).ToList();
        }

        var session = new LeagueSession(
            new LoadedLeague(loaded.Value),
            new RulesCapLedger(),
            new RulesDraftAssetLedger(),
            new RulesTradeEngine(),
            new RulesSigningEngine(),
            new RulesFreeAgencyMarket(),
            new RulesSeasonEngine(),
            new SaveGameSerializer(),
            new RulesFrontOfficeAdvisor(new RulesCapLedger()));

        var clock = Stopwatch.StartNew();
        string step = "loading the league";
        try
        {
            if (Failed(session.Load(), step, findings))
            {
                return findings;
            }

            step = "starting the season";
            if (Failed(session.StartSeason(seed), step, findings))
            {
                return findings;
            }

            step = "playing the season through";
            var played = session.AdvanceToEndOfSeason();
            if (Failed(played, step, findings))
            {
                return findings;
            }

            step = "concluding the season (awards, draft, re-signings)";
            var concluded = session.ConcludeSeason();
            if (Failed(concluded, step, findings))
            {
                return findings;
            }

            step = "opening the next season";
            if (Failed(session.StartSeason(seed + 1), step, findings))
            {
                return findings;
            }

            findings.Add(PackFinding.Info(
                "season.smoke_test_passed",
                PackCheckStage.Season,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Played a whole season ({played.Value.GamesPlayed:N0} games), concluded it ({concluded.Value.PlayersDrafted} drafted, {concluded.Value.PlayersAutoSigned} signed to fill rosters) and opened the next, in {clock.ElapsedMilliseconds} ms.")));
        }
#pragma warning disable CA1031 // The probe's whole job is to turn a crash on untrusted content into a finding.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            findings.Add(PackFinding.Error(
                "season.crashed",
                PackCheckStage.Season,
                $"The game crashed while {step}: {exception.GetType().Name}: {exception.Message}",
                fix: "This combination exposes a defect in the game rather than in a pack. Please report it with this check's report attached."));
        }

        return findings;
    }

    private LeagueRuleset? Read(string rulesetJson, out List<PackFinding> findings)
    {
        findings = [];
        if (string.IsNullOrWhiteSpace(rulesetJson))
        {
            findings.Add(PackFinding.Error("ruleset.empty", PackCheckStage.Settings, "The league settings resolved to an empty ruleset."));
            return null;
        }

        var result = _serializer.Deserialize(rulesetJson);
        if (result.IsSuccess)
        {
            return result.Value;
        }

        findings.AddRange(result.Errors.Select(error => PackFinding.Error(error.Code, PackCheckStage.Settings, error.Message)));
        return null;
    }

    private static DomainOperationResult<LeagueSnapshot> Load(LeagueSource source, LeagueRuleset ruleset)
    {
        switch (source)
        {
            case LeaguePackSource pack:
                return new LeaguePackDataSource(pack.PackFilePath, ruleset).Load();

            case GeneratedLeagueSource generated:
                var shape = GeneratedLeagueShape.Create(
                    generated.TeamCount,
                    generated.Conferences,
                    generated.DivisionsPerConference,
                    generated.RosterSize ?? Math.Min(ruleset.RosterLimits.MaximumPlayers, GeneratedLeagueShape.MaximumRosterSize));
                return shape.IsFailure
                    ? DomainOperationResult<LeagueSnapshot>.Failure(shape.Errors.ToArray())
                    : LeaguePackDataSource.FromEnvelope(GeneratedLeagueDataSource.Envelope(generated.Seed, shape.Value, ruleset), ruleset);

            default:
                throw new ArgumentOutOfRangeException(nameof(source), source, "Unknown league source.");
        }
    }

    /// <summary>
    /// How the loaded content sits against the chosen settings. None of this stops a load — a pack
    /// authored under other settings is still a league — but each is something the player would
    /// otherwise find out about mid-career: a team that cannot sign anyone, a star paid more than the
    /// settings allow anyone to be offered.
    /// </summary>
    private static List<PackFinding> Fit(LeagueSnapshot league, LeagueRuleset ruleset)
    {
        var findings = new List<PackFinding>();
        var players = league.Players.ToDictionary(player => player.Id);
        var salaries = league.Contracts
            .Select(contract => (contract, term: contract.TermFor(league.CurrentSeason)))
            .Where(entry => entry.term is not null && !entry.contract.IsTerminated)
            .ToList();

        var shortTeams = league.Teams
            .Where(team => team.RosterCount < ruleset.RosterLimits.MinimumPlayers)
            .OrderBy(team => team.Name, StringComparer.Ordinal)
            .Select(team => $"{team.Name} ({team.RosterCount})")
            .ToList();
        if (shortTeams.Count > 0)
        {
            findings.Add(PackFinding.Warning(
                "league.roster_below_minimum",
                PackCheckStage.League,
                $"{ExplanationText.Count(shortTeams.Count, "team")} {Verb(shortTeams.Count, "starts", "start")} below the {ruleset.RosterLimits.MinimumPlayers}-player roster minimum these settings require: {Examples(shortTeams)}.",
                fix: "Lower minimumRosterPlayers, or add players to the league pack."));
        }

        var softCap = ruleset.CapThresholds.SoftCap;
        var aboveCeiling = new List<string>();
        var belowFloor = new List<string>();
        var tooLong = new List<string>();
        foreach (var (contract, term) in salaries)
        {
            var player = players[contract.PlayerId];
            var ceiling = ruleset.NegotiationRules.CompensationCeiling.CeilingFor(player.SeasonsOfService, softCap);
            var floor = ruleset.NegotiationRules.CompensationFloor.FloorFor(player.SeasonsOfService);
            if (ceiling is not null && term!.Compensation.SmallestUnits > ceiling.SmallestUnits)
            {
                aboveCeiling.Add(player.FullName);
            }

            if (floor is not null && term!.Compensation.SmallestUnits < floor.SmallestUnits)
            {
                belowFloor.Add(player.FullName);
            }

            if (ruleset.NegotiationRules.MaximumIncumbentContractSeasons is int maximum && contract.Terms.Count > maximum)
            {
                tooLong.Add(player.FullName);
            }
        }

        if (aboveCeiling.Count > 0)
        {
            findings.Add(PackFinding.Warning(
                "league.salary_above_ceiling",
                PackCheckStage.League,
                $"{ExplanationText.Count(aboveCeiling.Count, "contract")} {Verb(aboveCeiling.Count, "pays", "pay")} more this season than these settings let anyone be offered for that player's service: {Examples(aboveCeiling)}. {Verb(aboveCeiling.Count, "It stands", "They stand")} as signed; nobody can be re-signed at those figures.",
                fix: "Raise the compensation ceiling (or the soft cap it is a share of), or use a league pack authored for these settings."));
        }

        if (belowFloor.Count > 0)
        {
            findings.Add(PackFinding.Warning(
                "league.salary_below_floor",
                PackCheckStage.League,
                $"{ExplanationText.Count(belowFloor.Count, "contract")} {Verb(belowFloor.Count, "pays", "pay")} less this season than these settings' minimum salary for that player's service: {Examples(belowFloor)}. {Verb(belowFloor.Count, "It stands", "They stand")} as signed.",
                fix: "Lower the compensation floor scale, or use a league pack authored for these settings."));
        }

        if (tooLong.Count > 0)
        {
            findings.Add(PackFinding.Info(
                "league.contract_longer_than_allowed",
                PackCheckStage.League,
                $"{ExplanationText.Count(tooLong.Count, "contract")} {Verb(tooLong.Count, "runs", "run")} longer than these settings allow any new contract to: {Examples(tooLong)}."));
        }

        var payrolls = league.Teams
            .Select(team => (team, payroll: salaries.Where(entry => entry.contract.TeamId == team.Id).Sum(entry => entry.term!.Compensation.SmallestUnits)))
            .OrderBy(entry => entry.team.Name, StringComparer.Ordinal)
            .ToList();

        if (ruleset.CapThresholds.HardCap is { } hardCap)
        {
            var over = payrolls.Where(entry => entry.payroll > hardCap.SmallestUnits).Select(entry => $"{entry.team.Name} ({ExplanationText.Money(entry.payroll)})").ToList();
            if (over.Count > 0)
            {
                findings.Add(PackFinding.Warning(
                    "league.payroll_above_hard_cap",
                    PackCheckStage.League,
                    $"{ExplanationText.Count(over.Count, "team")} {Verb(over.Count, "starts", "start")} above the {ExplanationText.Money(hardCap.SmallestUnits)} hard cap: {Examples(over)}. {(over.Count == 1 ? "It" : "They")} can sign nobody until {(over.Count == 1 ? "it sheds" : "they shed")} salary.",
                    fix: "Raise or remove hardCap, or use a league pack authored for these settings."));
            }
        }

        if (ruleset.CapThresholds.PayrollFloor is { } payrollFloor)
        {
            var under = payrolls.Where(entry => entry.payroll < payrollFloor.SmallestUnits).Select(entry => entry.team.Name).ToList();
            if (under.Count > 0)
            {
                findings.Add(PackFinding.Info(
                    "league.payroll_below_floor",
                    PackCheckStage.League,
                    $"{ExplanationText.Count(under.Count, "team")} {Verb(under.Count, "starts", "start")} below the {ExplanationText.Money(payrollFloor.SmallestUnits)} payroll floor: {Examples(under)}. {Verb(under.Count, "It is", "They are")} obliged to spend up to it; nothing stops the season."));
            }
        }

        var unsafeTeamKeys = league.Artwork.TeamKeys.Values.Where(key => !LeaguePackKeys.IsValid(key)).Order(StringComparer.Ordinal).ToList();
        if (unsafeTeamKeys.Count > 0)
        {
            findings.Add(PackFinding.Info(
                "league.team_key_not_file_safe",
                PackCheckStage.League,
                $"{ExplanationText.Count(unsafeTeamKeys.Count, "team key")} cannot be used as a file name ({Examples(unsafeTeamKeys)}), so a logo pack can reach those teams only through its explicit map, not by naming a file after the key."));
        }

        return findings;
    }

    private static bool Failed<T>(DomainOperationResult<T> result, string step, List<PackFinding> findings)
    {
        if (result.IsSuccess)
        {
            return false;
        }

        // A session reports one error per place it found it; the same sentence twenty times is one finding.
        findings.AddRange(result.Errors
            .DistinctBy(error => (error.Code, error.Message))
            .Select(error => PackFinding.Error(error.Code, PackCheckStage.Season, $"While {step}: {error.Message}")));
        return true;
    }


    private static string Verb(int count, string singular, string plural) => count == 1 ? singular : plural;

    private static string Examples(IReadOnlyList<string> names) =>
        names.Count <= ExampleCount
            ? string.Join(", ", names)
            : $"{string.Join(", ", names.Take(ExampleCount))} and {names.Count - ExampleCount} more";

    /// <summary>An already-loaded league handed to a session as its data source.</summary>
    private sealed class LoadedLeague(LeagueSnapshot snapshot) : ILeagueDataSource
    {
        public DomainOperationResult<LeagueSnapshot> Load() => DomainOperationResult<LeagueSnapshot>.Success(snapshot);
    }
}
