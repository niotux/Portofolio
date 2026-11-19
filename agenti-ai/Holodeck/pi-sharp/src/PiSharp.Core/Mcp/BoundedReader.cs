using System.Text;

namespace PiSharp.Core.Mcp;

/// <summary>Bounds a wire frame before JSON deserialization (images included).</summary>
internal static class BoundedReader
{
    internal const int MaxCharacters = 16 * 1024 * 1024;
    public static async Task<string> ReadToEndAsync(TextReader reader, CancellationToken ct)
    {
        var output = new StringBuilder(); var buffer = new char[8192];
        while (true)
        {
            var count = await reader.ReadAsync(buffer.AsMemory(), ct).ConfigureAwait(false);
            if (count == 0) return output.ToString();
            if (output.Length + count > MaxCharacters) throw new IOException("MCP response exceeds the 16 MiB character budget; narrow the query or use paged retrieval.");
            output.Append(buffer, 0, count);
        }
    }
}

internal sealed class BoundedLineReader(Stream stream)
{
    private readonly byte[] _buffer = new byte[8192];
    private int _position;
    private int _count;
    private readonly Decoder _decoder = new UTF8Encoding(false).GetDecoder();
    private readonly char[] _characters = new char[Encoding.UTF8.GetMaxCharCount(8192)];
    public async Task<string?> ReadLineAsync(CancellationToken ct)
    {
        var result = new StringBuilder();
        while (true)
        {
            if (_position == _count)
            {
                _count = await stream.ReadAsync(_buffer.AsMemory(), ct).ConfigureAwait(false);
                _position = 0;
                if (_count == 0)
                {
                    var final = _decoder.GetChars([], _characters, flush: true);
                    result.Append(_characters, 0, final);
                    return result.Length == 0 ? null : result.ToString();
                }
            }
            // Find newline in the buffer
            int end = -1;
            for (int i = _position; i < _count; i++)
            {
                if (_buffer[i] == (byte)'\n') { end = i; break; }
            }
            var length = (end < 0 ? _count : end) - _position;
            var count = _decoder.GetChars(_buffer.AsSpan(_position, length), _characters, flush: end >= 0);
            result.Append(_characters, 0, count);
            if (result.Length > BoundedReader.MaxCharacters) throw new IOException("MCP frame exceeds the 16 MiB character budget.");
            _position += length;
            if (end >= 0)
            {
                _position++;
                var line = result.ToString();
                // Strip trailing \r if present
                if (line.Length > 0 && line[^1] == '\r') line = line[..^1];
                return line;
            }
            if (result.Length > BoundedReader.MaxCharacters) throw new IOException("MCP frame exceeds the 16 MiB character budget.");
        }
    }
}
