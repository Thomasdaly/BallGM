using BallGM.Application.Leagues;
using BallGM.Domain.Common;
using BallGM.Domain.Negotiations;
using BallGM.Domain.Seasons;

namespace BallGM.Application.Saves;

/// <summary>
/// Everything a <c>LeagueSession</c> holds, read back out of a save: the league as
/// <see cref="ILeagueDataSource"/> would have handed it over, the season in progress if there was
/// one, and every in-flight negotiation keyed by player identifier — the same key
/// <c>LeagueSession</c> already uses internally, so loading it back in is a straight assignment.
/// </summary>
public sealed record SaveGameContents(
    LeagueSnapshot Snapshot,
    SeasonRun? Season,
    IReadOnlyDictionary<string, Negotiation> Negotiations);

/// <summary>
/// The port a <c>LeagueSession</c> reaches a save game through. Identical in shape to
/// <c>ICapLedger</c>, <c>IDraftAssetLedger</c>, <c>ITradeEngine</c>, <c>ISigningEngine</c>,
/// <c>IFreeAgencyMarket</c> and <c>Seasons.ISeasonEngine</c>, and for the same reason: turning the
/// pieces of a session into JSON and back is Infrastructure's job, and Application does not
/// reference it.
/// </summary>
public interface ISaveGameStore
{
    /// <summary>Writes a whole session — the league, the season in progress if any, and every open negotiation — as one save.</summary>
    DomainOperationResult<string> Save(
        LeagueSnapshot snapshot,
        SeasonRun? season,
        IReadOnlyDictionary<string, Negotiation> negotiations);

    /// <summary>Reads a save back. Replays every concept through its own aggregate's rule-checked methods.</summary>
    DomainOperationResult<SaveGameContents> Load(string json);
}
