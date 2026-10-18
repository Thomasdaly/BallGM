using System.Text.RegularExpressions;
using System.Xml.Linq;
using BallGM.Client.Avalonia.UiTests.Harness;

namespace BallGM.Client.Avalonia.UiTests.Release;

/// <summary>Source lints of spec 06 section 3: tokens only in views, compiled bindings everywhere.</summary>
[Trait("Category", "UiRelease")]
public sealed partial class LintTests
{
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void Tokens_NoHardcodedValues()
    {
        var views = UiLint.ClientFiles("*.axaml").ToList();
        Assert.NotEmpty(views);

        var violations = UiLint.Scan("tokens", views, HexColour())
            .Concat(UiLint.Scan("tokens", views, RgbFunction()))
            .Concat(UiLint.Scan("tokens", views, NamedColourAttribute(), match => match.Groups["value"].Value != "Transparent"))
            .Concat(UiLint.Scan("tokens", views, FontLiteral()));

        var unallowed = UiLint.Unallowed(violations);
        Assert.True(unallowed.Count == 0, "Hard-coded values outside Themes/:\n" + string.Join('\n', unallowed));
    }

    [Fact]
    public void Tokens_AllowlistEntriesCarryReasons()
    {
        var missing = UiLint.Allowlist().Where(entry => entry.Reason.Length == 0 || entry.Path.Length == 0).ToList();
        Assert.True(missing.Count == 0, "Allowlist entries without a path or reason:\n" + string.Join('\n', missing));
    }

    [Fact]
    public void Bindings_Compiled()
    {
        var project = File.ReadAllText(Path.Combine(UiLint.ClientRoot, "BallGM.Client.Avalonia.csproj"));
        Assert.Contains("<AvaloniaUseCompiledBindingsByDefault>true</AvaloniaUseCompiledBindingsByDefault>", project, StringComparison.Ordinal);

        var problems = new List<UiLint.Violation>();
        foreach (var file in UiLint.ClientFiles("*.axaml", includeThemes: true))
        {
            var document = XDocument.Load(file, LoadOptions.SetLineInfo);
            var relative = UiLint.Relative(file);
            var root = document.Root!;
            var isView = root.Name.LocalName is "UserControl" or "Window" or "TemplatedControl";
            if (isView && root.Attribute(Xaml + "DataType") is null)
            {
                problems.Add(new UiLint.Violation("bindings", relative, 1, $"<{root.Name.LocalName}> without x:DataType"));
            }

            foreach (var element in document.Descendants())
            {
                var line = ((System.Xml.IXmlLineInfo)element).LineNumber;
                if (element.Name.LocalName is "DataTemplate" or "TreeDataTemplate"
                    && element.Attribute(Xaml + "DataType") is null && element.Attribute("DataType") is null)
                {
                    problems.Add(new UiLint.Violation("bindings", relative, line, $"<{element.Name.LocalName}> without x:DataType"));
                }

                if (element.Attribute(Xaml + "CompileBindings") is { Value: "False" or "false" })
                {
                    problems.Add(new UiLint.Violation("bindings", relative, line, "x:CompileBindings=\"False\""));
                }

                foreach (var attribute in element.Attributes().Where(attribute => attribute.Value.Contains("ReflectionBinding", StringComparison.Ordinal)))
                {
                    problems.Add(new UiLint.Violation("bindings", relative, line, attribute.Value));
                }
            }
        }

        var unallowed = UiLint.Unallowed(problems);
        Assert.True(unallowed.Count == 0, "Bindings that are not compiled:\n" + string.Join('\n', unallowed));
    }

    [GeneratedRegex(@"#[0-9A-Fa-f]{3,8}\b")]
    private static partial Regex HexColour();

    [GeneratedRegex(@"\brgba?\s*\(", RegexOptions.IgnoreCase)]
    private static partial Regex RgbFunction();

    [GeneratedRegex(@"\b(?:Background|Foreground|Fill|Stroke|BorderBrush|Color|BorderColor|[A-Za-z]+Brush)=""(?<value>[A-Za-z]+)""")]
    private static partial Regex NamedColourAttribute();

    [GeneratedRegex(@"\b(?:FontSize|FontFamily)=""(?!\{)[^""]*""")]
    private static partial Regex FontLiteral();
}
