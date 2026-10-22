using System.Globalization;
using System.Text;
using BallGM.Application.Packs;
using BallGM.Domain.Common;

namespace BallGM.Mods.Checking;

/// <summary>
/// A report as plain text: what the command-line checker prints and what "copy report" puts on the
/// clipboard for a bug report. The verdict first, then the load plan, then the findings worst first
/// within each stage — a player reads the first line, a pack author reads the rest.
/// </summary>
public static class PackCheckReportText
{
    public static string Format(PackCheckReport report, bool includeInfo = true)
    {
        ArgumentNullException.ThrowIfNull(report);
        var text = new StringBuilder();

        text.AppendLine(CultureInfo.InvariantCulture, $"Pack check: {report.SetupName}");
        text.AppendLine(CultureInfo.InvariantCulture, $"Verdict: {Verdict(report.Verdict)}");
        var errors = report.Errors.Count();
        var warnings = report.Warnings.Count();
        text.AppendLine(CultureInfo.InvariantCulture, $"{ExplanationText.Count(errors, "error")}, {ExplanationText.Count(warnings, "warning")}, {ExplanationText.Count(report.Findings.Count - errors - warnings, "note")}.");

        if (report.LoadPlan.Count > 0)
        {
            text.AppendLine();
            text.AppendLine("Load order:");
            foreach (var step in report.LoadPlan)
            {
                text.AppendLine(CultureInfo.InvariantCulture, $"  {step.Order}. [{step.Stage}] {step.Description}");
            }
        }

        if (report.Coverage.Count > 0)
        {
            text.AppendLine();
            text.AppendLine("Coverage: " + string.Join(", ", report.Coverage.Select(item => string.Create(CultureInfo.InvariantCulture, $"{item.Kind} {item.Covered:N0}/{item.Total:N0}"))));
        }

        foreach (var stage in Enum.GetValues<PackCheckStage>())
        {
            var findings = report.Findings
                .Where(finding => finding.Stage == stage && (includeInfo || finding.Severity != PackFindingSeverity.Info))
                .OrderByDescending(finding => finding.Severity)
                .ToList();
            if (findings.Count == 0)
            {
                continue;
            }

            text.AppendLine();
            text.AppendLine(CultureInfo.InvariantCulture, $"{stage}:");
            foreach (var finding in findings)
            {
                var pack = finding.PackId is null ? string.Empty : $" [{finding.PackId}]";
                text.AppendLine(CultureInfo.InvariantCulture, $"  {Mark(finding.Severity)} {finding.Code}{pack}: {finding.Message}");
                if (finding.Fix is not null)
                {
                    text.AppendLine(CultureInfo.InvariantCulture, $"      fix: {finding.Fix}");
                }
            }
        }

        return text.ToString();
    }

    public static string Verdict(PackCheckVerdict verdict) => verdict switch
    {
        PackCheckVerdict.Stable => "STABLE — loads, plays, and looks the way the packs say.",
        PackCheckVerdict.StableWithWarnings => "STABLE WITH WARNINGS — loads and plays; read the warnings for what will look or behave differently.",
        _ => "BLOCKED — the game will not start this setup until the errors are fixed.",
    };

    private static string Mark(PackFindingSeverity severity) => severity switch
    {
        PackFindingSeverity.Error => "ERROR",
        PackFindingSeverity.Warning => "WARN ",
        _ => "note ",
    };
}
