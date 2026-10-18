using BallGM.Application.Leagues;
using BallGM.Client.Avalonia.Converters;

namespace BallGM.Client.Avalonia.Imaging;

/// <summary>
/// Builds the resolver's catalog from the loaded league: the pack's own logo and portrait mapping,
/// team colours for every fallback, and each entity's short label. The game has one mod kind - the
/// league pack, whose entries name their images - so it is listed as the one pack.
/// </summary>
public static class AssetCatalogBuilder
{
    public static AssetCatalog Build(LeagueOverview overview, LeagueArtwork artwork, string? packFilePath)
    {
        ArgumentNullException.ThrowIfNull(overview);
        ArgumentNullException.ThrowIfNull(artwork);

        var packRoot = packFilePath is null ? null : Path.GetDirectoryName(Path.GetFullPath(packFilePath));
        var packName = packFilePath is null ? null : overview.LeagueName;
        var entries = new List<AssetEntry>();
        var teamOfPlayer = new Dictionary<string, TeamSummary>(StringComparer.Ordinal);

        foreach (var team in overview.Teams)
        {
            entries.Add(new AssetEntry(
                new AssetKey(AssetEntityKind.Team, team.TeamId),
                TeamInitialsConverter.Initials(team.TeamName),
                team.LogoPath ?? artwork.TeamLogos.GetValueOrDefault(team.TeamId),
                packName,
                packRoot,
                team.Colours?.Primary,
                team.Colours?.Secondary));
            foreach (var spot in team.Roster)
            {
                teamOfPlayer[spot.PlayerId] = team;
            }
        }

        var names = overview.Teams.SelectMany(team => team.Roster).ToDictionary(spot => spot.PlayerId, spot => spot.FullName, StringComparer.Ordinal);
        foreach (var freeAgent in overview.FreeAgents.Players)
        {
            names.TryAdd(freeAgent.PlayerId, freeAgent.FullName);
        }

        foreach (var (playerId, name) in names)
        {
            var team = teamOfPlayer.GetValueOrDefault(playerId);
            entries.Add(new AssetEntry(
                new AssetKey(AssetEntityKind.Player, playerId),
                Initials(name),
                artwork.PlayerPortraits.GetValueOrDefault(playerId),
                packName,
                packRoot,
                team?.Colours?.Primary,
                team?.Colours?.Secondary));
        }

        entries.Add(new AssetEntry(new AssetKey(AssetEntityKind.League, overview.LeagueName), TeamInitialsConverter.Initials(overview.LeagueName), null, packName, packRoot, null, null));

        var packs = packFilePath is null
            ? Array.Empty<AssetPack>()
            : [new AssetPack(overview.LeagueName, "League pack (roster, faces, logos)", Path.GetFullPath(packFilePath), 1)];
        return new AssetCatalog(entries, packs);
    }

    private static string Initials(string name)
    {
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length switch
        {
            0 => string.Empty,
            1 => parts[0][..Math.Min(2, parts[0].Length)],
            _ => string.Concat(parts[0][0], parts[^1][0]),
        };
    }
}
