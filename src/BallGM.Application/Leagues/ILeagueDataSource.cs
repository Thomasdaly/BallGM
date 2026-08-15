using BallGM.Domain.Common;

namespace BallGM.Application.Leagues;

/// <summary>
/// Supplies a fully loaded league to the Application layer. The implementation decides where the
/// league comes from — a fixture, a ruleset file plus generated content, or a save — and lives in
/// <c>BallGM.Infrastructure</c> so that filesystem access never leaks into Application or Domain.
/// Loading can legitimately fail on untrusted content, so it returns a structured result rather
/// than throwing.
/// </summary>
public interface ILeagueDataSource
{
    DomainOperationResult<LeagueSnapshot> Load();
}
