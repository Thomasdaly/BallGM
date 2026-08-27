using System.Text;
using BallGM.Application.Leagues;
using BallGM.Infrastructure.Cap;
using BallGM.Infrastructure.DraftAssets;
using BallGM.Infrastructure.Fixtures;
using BallGM.Infrastructure.Negotiations;

namespace BallGM.Integration.Tests;

public sealed class BoardDumpProbe
{
    [Fact]
    public void Dump()
    {
        var result = new GetLeagueOverviewQuery(new FixtureLeagueDataSource(), new RulesCapLedger(), new RulesDraftAssetLedger(), new RulesSigningEngine()).Execute();
        Assert.True(result.IsSuccess, string.Join("; ", result.Errors.Select(error => error.Message)));

        var text = new StringBuilder();
        foreach (var row in result.Value.PickBoard.Franchises)
        {
            text.AppendLine($"== {row.FranchiseName}");
            foreach (var cell in row.Drafts)
            {
                foreach (var asset in cell.Assets)
                {
                    text.AppendLine($"  {cell.DraftSeason} {asset.Label} [{asset.State}] {asset.ProtectionSummary} || {asset.OutcomeIfProtectionHolds} || history={asset.History.Count}");
                }
            }
        }

        File.WriteAllText("/private/tmp/claude-501/-Users-tomdaly-repos-BallGM/b33193d7-a907-47bb-ae14-1fdb15693984/scratchpad/board.txt", text.ToString());
    }
}
