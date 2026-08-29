using BallGM.Domain.Common;
using BallGM.Domain.Players;
using BallGM.Domain.Teams;

namespace BallGM.Domain.Seasons;

/// <summary>
/// One player's place in a team's rotation: which position they are being used at, how far down the
/// chart they are there, and how many minutes a game that buys them.
/// <para>
/// <see cref="DepthRank"/> counts from 1 — the starter at that position — so "who starts" is a
/// property of the chart rather than a second flag that could disagree with it.
/// </para>
/// </summary>
public sealed record DepthChartSlot
{
    public DepthChartSlot(PlayerId playerId, Position position, int depthRank, int minutes)
    {
        ArgumentNullException.ThrowIfNull(playerId);

        if (!Enum.IsDefined(position))
        {
            throw new ArgumentOutOfRangeException(nameof(position), position, "Position must be a defined basketball position.");
        }

        if (depthRank < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(depthRank), depthRank, "Depth rank counts from 1, where 1 is the starter.");
        }

        if (minutes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(minutes), minutes, "Allotted minutes cannot be negative.");
        }

        PlayerId = playerId;
        Position = position;
        DepthRank = depthRank;
        Minutes = minutes;
    }

    public PlayerId PlayerId { get; }

    public Position Position { get; }

    public int DepthRank { get; }

    public int Minutes { get; }

    public bool IsStarter => DepthRank == 1;
}

/// <summary>
/// A team's rotation for one game: every available player, the position they are used at, and the
/// minutes they get.
/// <para>
/// Columned by position on purpose, and by the <em>same</em> positional concept the Milestone 6b
/// free-agency board reads a market against. A GM looks at their depth at a position to decide
/// whether to sign there, and the simulation looks at their depth at a position to decide who
/// plays; two different notions of "depth at the three" would be two answers to one question, and
/// the market screen would be advising against a squad the engine does not field.
/// </para>
/// <para>
/// Which players are available and how the minutes divide is a rules decision, so the chart is
/// <em>built</em> in <c>BallGM.Rules.Seasons</c>. What the aggregate guarantees is that the chart is
/// coherent: nobody appears twice, and each position's ranks run 1, 2, 3 without a gap.
/// </para>
/// </summary>
public sealed class DepthChart
{
    private const string DuplicatePlayerCode = "depth_chart.player_listed_twice";
    private const string DepthRanksNotContiguousCode = "depth_chart.depth_ranks_not_contiguous";

    private readonly List<DepthChartSlot> _slots;

    private DepthChart(TeamId teamId, List<DepthChartSlot> slots)
    {
        TeamId = teamId;
        _slots = slots;
    }

    /// <summary>A team with nobody available. What a roster stripped to nothing produces, and a real state.</summary>
    public static DepthChart Empty(TeamId teamId)
    {
        ArgumentNullException.ThrowIfNull(teamId);
        return new DepthChart(teamId, []);
    }

    public static DomainOperationResult<DepthChart> Create(TeamId teamId, IEnumerable<DepthChartSlot> slots)
    {
        ArgumentNullException.ThrowIfNull(teamId);
        ArgumentNullException.ThrowIfNull(slots);

        var supplied = slots.ToList();
        if (supplied.Any(slot => slot is null))
        {
            throw new ArgumentException("A depth chart cannot contain null slots.", nameof(slots));
        }

        var errors = new List<DomainError>();

        foreach (var group in supplied.GroupBy(slot => slot.PlayerId).Where(group => group.Count() > 1))
        {
            errors.Add(new DomainError(
                DuplicatePlayerCode,
                $"Player '{group.Key.Value}' appears {group.Count()} times on team '{teamId.Value}'s depth chart. A player takes one place in a rotation."));
        }

        foreach (var group in supplied.GroupBy(slot => slot.Position))
        {
            var ranks = group.Select(slot => slot.DepthRank).OrderBy(rank => rank).ToArray();
            var expected = Enumerable.Range(1, ranks.Length).ToArray();

            if (!ranks.SequenceEqual(expected))
            {
                errors.Add(new DomainError(
                    DepthRanksNotContiguousCode,
                    $"Depth at {group.Key} on team '{teamId.Value}' is ranked {string.Join(", ", ranks)} rather than {string.Join(", ", expected)}. A chart with a gap in it has a position with no starter or a rank nobody holds."));
            }
        }

        if (errors.Count > 0)
        {
            return DomainOperationResult<DepthChart>.Failure(errors.ToArray());
        }

        var ordered = supplied
            .OrderBy(slot => slot.Position)
            .ThenBy(slot => slot.DepthRank)
            .ToList();

        return DomainOperationResult<DepthChart>.Success(new DepthChart(teamId, ordered));
    }

    public TeamId TeamId { get; }

    public IReadOnlyList<DepthChartSlot> Slots => _slots;

    public int PlayerCount => _slots.Count;

    public bool IsEmpty => _slots.Count == 0;

    public int TotalMinutes => _slots.Sum(slot => slot.Minutes);

    public IReadOnlyList<DepthChartSlot> At(Position position) =>
        _slots.Where(slot => slot.Position == position).ToArray();

    public int DepthAt(Position position) => _slots.Count(slot => slot.Position == position);

    public IReadOnlyList<DepthChartSlot> Starters =>
        _slots.Where(slot => slot.IsStarter).OrderBy(slot => slot.Position).ToArray();

    public DepthChartSlot? SlotFor(PlayerId playerId)
    {
        ArgumentNullException.ThrowIfNull(playerId);
        return _slots.FirstOrDefault(slot => slot.PlayerId == playerId);
    }
}
