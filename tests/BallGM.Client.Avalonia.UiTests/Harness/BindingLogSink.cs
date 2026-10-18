using Avalonia.Logging;

namespace BallGM.Client.Avalonia.UiTests.Harness;

/// <summary>
/// Records binding-area warnings and errors while a test runs, so a binding mistake fails the test
/// instead of rendering a silent blank (docs/ui/spec/02_AVALONIA_GUIDE.md section 15).
/// </summary>
internal sealed class BindingLogSink : ILogSink
{
    private readonly List<string> _messages = [];
    private readonly Lock _gate = new();

    public IReadOnlyList<string> Messages
    {
        get
        {
            lock (_gate)
            {
                return _messages.ToArray();
            }
        }
    }

    public static BindingLogSink Install()
    {
        var sink = new BindingLogSink();
        Logger.Sink = sink;
        return sink;
    }

    public void Clear()
    {
        lock (_gate)
        {
            _messages.Clear();
        }
    }

    public bool IsEnabled(LogEventLevel level, string area) =>
        level >= LogEventLevel.Warning && area == LogArea.Binding;

    public void Log(LogEventLevel level, string area, object? source, string messageTemplate) =>
        Log(level, area, source, messageTemplate, []);

    public void Log(LogEventLevel level, string area, object? source, string messageTemplate, params object?[] propertyValues)
    {
        if (!IsEnabled(level, area))
        {
            return;
        }

        var rendered = messageTemplate;
        for (var index = 0; index < propertyValues.Length; index++)
        {
            var open = rendered.IndexOf('{', StringComparison.Ordinal);
            var close = open < 0 ? -1 : rendered.IndexOf('}', open);
            if (close < 0)
            {
                break;
            }

            rendered = string.Concat(rendered.AsSpan(0, open), propertyValues[index]?.ToString() ?? "null", rendered.AsSpan(close + 1));
        }

        lock (_gate)
        {
            _messages.Add($"[{level}] {source?.GetType().Name}: {rendered}");
        }
    }
}
