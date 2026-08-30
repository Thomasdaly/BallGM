using BallGM.Application.Leagues;
using BallGM.Application.Saves;
using BallGM.Domain.Common;
using BallGM.Domain.Negotiations;
using BallGM.Domain.Seasons;

namespace BallGM.Application.Tests;

/// <summary>
/// A save store the session tests can construct without Infrastructure. Nothing here exercises save
/// or load; the session tests are about orchestration, and the real round trip is proved against
/// <c>SaveGameSerializer</c> in the integration suite.
/// </summary>
internal sealed class StubSaveGameStore : ISaveGameStore
{
    public DomainOperationResult<string> Save(
        LeagueSnapshot snapshot,
        SeasonRun? season,
        IReadOnlyDictionary<string, Negotiation> negotiations) =>
        DomainOperationResult<string>.Success(string.Empty);

    public DomainOperationResult<SaveGameContents> Load(string json) =>
        throw new NotSupportedException("StubSaveGameStore does not support loading; construct a session with a real ISaveGameStore for load tests.");
}
