using BallGM.Domain.Players;

namespace BallGM.Domain.Seasons;

/// <summary>
/// One player's totals across every game of one season — the "career and season statistics" this
/// build's `docs/architecture.md` named as owed once a real match engine existed to produce
/// <see cref="PlayerStatLine"/>s to sum. Every counting stat, as <see cref="CountingStats"/>, so nothing
/// the engine credits is dropped here on its way to a screen; totals only, because a rate is a
/// derivation <c>StatFormulas</c> owns.
/// <para>
/// This is a re-derived value, not stored state — the same "the champion is re-derived, never
/// stored" reasoning `SeasonConclusion` already applies. A season's <see cref="BoxScore"/>s are
/// already the source of truth on <see cref="SeasonRun"/>; summing them again after a load costs
/// nothing a save would otherwise have to keep reconciled.
/// </para>
/// </summary>
public sealed record PlayerSeasonStatLine(PlayerId PlayerId, CountingStats Totals);
