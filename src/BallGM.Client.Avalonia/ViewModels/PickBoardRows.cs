using BallGM.Application.Leagues;

namespace BallGM.Client.Avalonia.ViewModels;

/// <summary>
/// One pick in a board cell, squeezed down to what fits in a grid square. The protection is kept on
/// the chip rather than left to the drill-down: a board that shows ownership but hides protection is
/// the board that gets a GM traded into a lottery they already sold.
/// </summary>
public sealed record PickChip(string Label, string State, string? Protection, bool IsConditional)
{
    public static PickChip From(PickAssetSummary asset)
    {
        ArgumentNullException.ThrowIfNull(asset);

        return new PickChip(
            $"R{asset.Round}",
            asset.State,
            Shorten(asset.ProtectionSummary),
            asset.ProtectionSummary is not null);
    }

    /// <summary>
    /// The cell shows the condition, not the whole clause — the sentence in full is one click away
    /// in the asset list, where there is room to read it.
    /// </summary>
    private static string? Shorten(string? protection)
    {
        if (protection is null)
        {
            return null;
        }

        var firstSentenceEnd = protection.IndexOf(". ", StringComparison.Ordinal);
        return firstSentenceEnd > 0 ? protection[..(firstSentenceEnd + 1)] : protection;
    }
}

/// <summary>One franchise's holdings in one future draft.</summary>
public sealed record PickCellRow(int DraftSeason, IReadOnlyList<PickChip> Assets)
{
    public static PickCellRow From(FranchisePickCell cell)
    {
        ArgumentNullException.ThrowIfNull(cell);
        return new PickCellRow(cell.DraftSeason, cell.Assets.Select(PickChip.From).ToList());
    }

    public bool IsEmpty => Assets.Count == 0;
}

/// <summary>One franchise's row across the board.</summary>
public sealed record PickBoardRow(string FranchiseId, string FranchiseName, IReadOnlyList<PickCellRow> Cells)
{
    public static PickBoardRow From(FranchisePickRow row)
    {
        ArgumentNullException.ThrowIfNull(row);

        return new PickBoardRow(
            row.FranchiseId,
            row.FranchiseName,
            row.Drafts.Select(PickCellRow.From).ToList());
    }
}

/// <summary>
/// One asset in the drill-down list: the full protection clause, what happens if that protection
/// holds, and the ledger trail behind it.
/// </summary>
public sealed record PickAssetRow(
    string PickId,
    string Title,
    string State,
    string? Protection,
    string? OutcomeIfProtectionHolds,
    string OwnerLine,
    IReadOnlyList<LedgerRow> History)
{
    public static PickAssetRow From(int draftSeason, PickAssetSummary asset)
    {
        ArgumentNullException.ThrowIfNull(asset);

        return new PickAssetRow(
            asset.PickId,
            $"{draftSeason} · {asset.Label}",
            asset.State,
            asset.ProtectionSummary,
            asset.OutcomeIfProtectionHolds,
            $"Originally {asset.OriginalFranchiseName} · currently controlled by {asset.CurrentOwnerName}",
            asset.History.Select(LedgerRow.From).ToList());
    }

    public bool HasHistory => History.Count > 0;

    public bool HasProtection => Protection is not null;

    public override string ToString() => Title;
}
