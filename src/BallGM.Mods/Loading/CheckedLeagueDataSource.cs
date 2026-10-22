using BallGM.Application.Leagues;
using BallGM.Application.Packs;
using BallGM.Domain.Common;
using BallGM.Mods.Checking;

namespace BallGM.Mods.Loading;

/// <summary>
/// The league a checked setup describes, as an <see cref="ILeagueDataSource"/>: the settings the
/// checker resolved, the league it probed, and the cosmetic winners it chose, re-applied to a fresh
/// load. A setup the checker blocked does not load at all, and says why with the checker's own
/// findings.
/// <para>
/// Cosmetics are matched by pack key, never by loaded identifier: identifiers are minted fresh on
/// every load, keys are what the packs were written against, and the league's own
/// <see cref="LeagueArtwork.TeamKeys"/>/<see cref="LeagueArtwork.PlayerKeys"/> translate one to the
/// other on each load.
/// </para>
/// </summary>
public sealed class CheckedLeagueDataSource : ILeagueDataSource
{
    private readonly PackCheckReport _report;
    private readonly IPackContentProbe _probe;

    public CheckedLeagueDataSource(PackCheckReport report, IPackContentProbe probe)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(probe);
        _report = report;
        _probe = probe;
    }

    public DomainOperationResult<LeagueSnapshot> Load()
    {
        if (_report.Plan is not { } plan)
        {
            var errors = _report.Errors.Select(finding => new DomainError(finding.Code, finding.Message)).ToArray();
            return DomainOperationResult<LeagueSnapshot>.Failure(errors.Length > 0
                ? errors
                : [new DomainError("setup.blocked", $"The setup '{_report.SetupName}' did not pass the pack check.")]);
        }

        var loaded = _probe.ProbeLeague(plan.League, plan.RulesetJson);
        if (!loaded.Loaded)
        {
            return DomainOperationResult<LeagueSnapshot>.Failure(
                loaded.Findings
                    .Where(finding => finding.Severity == PackFindingSeverity.Error)
                    .Select(finding => new DomainError(finding.Code, finding.Message))
                    .DefaultIfEmpty(new DomainError("setup.league_did_not_load", "The league loaded when it was checked and does not load now; check the setup again."))
                    .ToArray());
        }

        var snapshot = loaded.Value!;
        var artwork = snapshot.Artwork;
        var teamIds = artwork.TeamKeys.ToDictionary(entry => entry.Value, entry => entry.Key, StringComparer.Ordinal);
        var playerIds = artwork.PlayerKeys.ToDictionary(entry => entry.Value, entry => entry.Key, StringComparer.Ordinal);

        var layered = new LeagueArtwork(
            Rekey(plan.TeamLogos, teamIds),
            Rekey(plan.PlayerPortraits, playerIds))
        {
            TeamColours = Rekey(plan.TeamColours, teamIds),
            TeamKeys = artwork.TeamKeys,
            PlayerKeys = artwork.PlayerKeys,
            LeagueLogo = plan.LeagueLogo,
        };

        return DomainOperationResult<LeagueSnapshot>.Success(snapshot with { Artwork = layered });
    }

    private static Dictionary<string, T> Rekey<T>(IReadOnlyDictionary<string, T> byKey, IReadOnlyDictionary<string, string> idsByKey) =>
        byKey
            .Where(entry => idsByKey.ContainsKey(entry.Key))
            .ToDictionary(entry => idsByKey[entry.Key], entry => entry.Value, StringComparer.Ordinal);
}
