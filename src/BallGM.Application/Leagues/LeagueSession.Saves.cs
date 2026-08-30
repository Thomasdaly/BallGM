using BallGM.Domain.Common;

namespace BallGM.Application.Leagues;

/// <summary>
/// The save-game half of the session: writing everything it holds out as one file, and reading it
/// back in.
/// <para>
/// Deliberately thin. <see cref="Application.Saves.ISaveGameStore"/> does the actual composing and
/// replaying — the same trust boundary every other engine this session holds occupies — so this file
/// only has to gather what a save needs (the snapshot, the season if one is running, every open
/// negotiation) and hand back what a load produces.
/// </para>
/// </summary>
public sealed partial class LeagueSession
{
    /// <summary>Writes this session — the league, the season in progress if any, and every open negotiation — as one save.</summary>
    public DomainOperationResult<string> Save()
    {
        if (_snapshot is null)
        {
            return NotLoaded<string>();
        }

        return _saveGameStore.Save(_snapshot, _seasonRun, _negotiations);
    }

    /// <summary>
    /// Loads a save, replacing everything this session currently holds. Calling it on a session
    /// already running a league discards that league's unsaved state, the same way <see cref="Load"/>
    /// does.
    /// </summary>
    public DomainOperationResult<LeagueOverview> LoadSave(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        var loaded = _saveGameStore.Load(json);
        if (loaded.IsFailure)
        {
            return DomainOperationResult<LeagueOverview>.Failure(loaded.Errors.ToArray());
        }

        _snapshot = loaded.Value.Snapshot;
        _seasonRun = loaded.Value.Season;

        _negotiations.Clear();
        foreach (var (playerId, negotiation) in loaded.Value.Negotiations)
        {
            _negotiations[playerId] = negotiation;
        }

        return _overviewQuery.Project(_snapshot);
    }
}
