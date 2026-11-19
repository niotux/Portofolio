// Minimal ANSI helpers. Honors NO_COLOR and redirected stdout. No third-party library.
namespace PiSharp.Cli;

public sealed class Ansi
{
    public bool Enabled { get; }

    public Ansi(bool? enabled = null)
    {
        Enabled = enabled ?? (Environment.GetEnvironmentVariable("NO_COLOR") is null
                              && !Console.IsOutputRedirected);
    }

    public string Reset => Wrap("0");
    public string Bold => Wrap("1");
    public string Dim => Wrap("2");
    public string Red => Wrap("31");
    public string Green => Wrap("32");
    public string Yellow => Wrap("33");
    public string Blue => Wrap("34");
    public string Magenta => Wrap("35");
    public string Cyan => Wrap("36");
    public string Gray => Wrap("90");

    public string ColorizeDiff(string diff)
    {
        if (!Enabled) return diff;
        var lines = diff.Replace("\r\n", "\n").Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line.StartsWith("+++") || line.StartsWith("---") || line.StartsWith("@@"))
                lines[i] = Cyan + line + Reset;
            else if (line.StartsWith('+'))
                lines[i] = Green + line + Reset;
            else if (line.StartsWith('-'))
                lines[i] = Red + line + Reset;
        }
        return string.Join('\n', lines);
    }

    private string Wrap(string code) => Enabled ? "\u001b[" + code + "m" : "";
}
