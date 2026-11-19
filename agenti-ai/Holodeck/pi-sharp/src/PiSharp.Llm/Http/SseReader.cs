// Hand-written Server-Sent Events reader (replaces the openai npm SDK's stream decoder).
// Follows the WHATWG EventSource parsing rules for the subset chat-completion servers use:
// `data:` lines, blank-line event separators, `:` comment lines, CRLF or LF line endings.
using System.Runtime.CompilerServices;
using System.Text;

namespace PiSharp.Llm.Http;

public static class SseReader
{
    /// <summary>
    /// Yields the <c>data</c> payload of each event. Multi-line data is joined with '\n'.
    /// The literal <c>[DONE]</c> sentinel ends the sequence.
    /// </summary>
    public static async IAsyncEnumerable<string> ReadDataAsync(Stream stream, [EnumeratorCancellation] CancellationToken ct = default)
    {
        using var reader = new StreamReader(stream, new UTF8Encoding(false), detectEncodingFromByteOrderMarks: false, bufferSize: 16 * 1024, leaveOpen: true);
        var data = new StringBuilder();
        var hasData = false;

        while (true)
        {
            var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
            if (line is null)
            {
                if (hasData) yield return data.ToString();
                yield break;
            }

            if (line.Length == 0)
            {
                if (hasData)
                {
                    var payload = data.ToString();
                    data.Clear();
                    hasData = false;
                    if (payload == "[DONE]") yield break;
                    yield return payload;
                }
                continue;
            }

            if (line[0] == ':') continue; // comment / keep-alive

            string field, value;
            var colon = line.IndexOf(':');
            if (colon < 0)
            {
                field = line;
                value = "";
            }
            else
            {
                field = line[..colon];
                value = line[(colon + 1)..];
                if (value.Length > 0 && value[0] == ' ') value = value[1..];
            }

            if (field == "data")
            {
                if (hasData) data.Append('\n');
                data.Append(value);
                hasData = true;
            }
            // `event`, `id` and `retry` are ignored: chat-completion servers do not use them.
        }
    }
}
