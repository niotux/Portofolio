using System.Globalization;
using System.Text;

namespace PiSharp.Cli;

/// <summary>
/// Begins every line with its UTC time ("2026-10-30T04:12:05Z "). A seat's stderr is its log file
/// (/var/log/pi-seat/piN.log), and until 2026-10-30 its lines had no time: nobody could say when pi13 went quiet or what it
/// did before (digital-scribe, 2026-10-30: "also 3"). Only a redirected stderr gets it, so a person at a terminal sees
/// the lines as before.
/// </summary>
public sealed class StampedWriter(TextWriter inner, Func<DateTimeOffset>? clock = null) : TextWriter
{
    private readonly Func<DateTimeOffset> _clock = clock ?? (() => DateTimeOffset.UtcNow);
    private bool _lineStart = true;

    public override Encoding Encoding => inner.Encoding;

    public override void Write(char value)
    {
        if (_lineStart && value != '\n') inner.Write(Stamp());
        inner.Write(value);
        _lineStart = value == '\n';
    }

    public override void Write(string? value)
    {
        if (string.IsNullOrEmpty(value)) return;
        var text = new StringBuilder(value.Length + 32);
        foreach (var c in value)
        {
            if (_lineStart && c != '\n') text.Append(Stamp());
            text.Append(c);
            _lineStart = c == '\n';
        }
        inner.Write(text.ToString());
    }

    public override void WriteLine(string? value)
    {
        Write(value);
        Write('\n');
    }

    public override void WriteLine() => Write('\n');

    public override void Flush() => inner.Flush();

    private string Stamp() => _clock().UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z' ", CultureInfo.InvariantCulture);

    protected override void Dispose(bool disposing)
    {
        if (disposing) inner.Flush();
        base.Dispose(disposing);
    }
}
