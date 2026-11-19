// Ported from pi: packages/coding-agent/src/core/tools/output-accumulator.ts @ 71dca871b
using System.Security.Cryptography;
using System.Text;

namespace PiSharp.Tools;

/// <summary>A display snapshot of accumulated shell output.</summary>
public sealed record OutputSnapshot(string Content, TruncationResult Truncation, string? FullOutputPath);

/// <summary>
/// Incrementally tracks streaming output with bounded memory.
/// Decodes UTF-8 in streaming fashion, keeps a decoded tail for display, and
/// opens a temp file when the full output needs to be preserved.
/// </summary>
public sealed class OutputAccumulator : IDisposable
{
    private readonly int _maxLines;
    private readonly int _maxBytes;
    private readonly int _maxRollingBytes;
    private readonly string _tempFilePrefix;
    private readonly Decoder _decoder = Encoding.UTF8.GetDecoder();

    private readonly List<byte[]> _rawChunks = [];
    private string _headText = "";
    private int _headBytes;
    private string _tailText = "";
    private int _tailBytes;
    private bool _tailStartsAtLineBoundary = true;
    private long _totalRawBytes;
    private long _totalDecodedBytes;
    private int _completedLines;
    private int _totalLines;
    private int _currentLineBytes;
    private bool _hasOpenLine;
    private bool _finished;

    private string? _tempFilePath;
    private FileStream? _tempFileStream;

    public OutputAccumulator(int? maxLines = null, int? maxBytes = null, string tempFilePrefix = "pi-output")
    {
        _maxLines = maxLines ?? Truncation.DefaultMaxLines;
        _maxBytes = maxBytes ?? Truncation.DefaultMaxBytes;
        _maxRollingBytes = Math.Max(_maxBytes * 2, 1);
        _tempFilePrefix = tempFilePrefix;
    }

    public void Append(ReadOnlySpan<byte> data, string? decoded = null)
    {
        if (_finished) throw new InvalidOperationException("Cannot append to a finished output accumulator");
        if (data.Length == 0)
        { if (decoded is not null) AppendDecodedText(decoded); return; }
        if (_totalRawBytes + data.Length > 64L * 1024 * 1024)
            throw new IOException("Command output exceeded 64 MiB; command stopped. Existing output remains available.");

        _totalRawBytes += data.Length;
        AppendDecodedText(decoded ?? Decode(data, flush: false));

        if (_tempFileStream is not null || ShouldUseTempFile())
        {
            EnsureTempFile();
            _tempFileStream!.Write(data);
        }
        else
        {
            _rawChunks.Add(data.ToArray());
        }
    }

    public void Finish()
    {
        if (_finished) return;
        _finished = true;
        AppendDecodedText(Decode(ReadOnlySpan<byte>.Empty, flush: true));
        if (ShouldUseTempFile()) EnsureTempFile();
    }

    public OutputSnapshot Snapshot(bool persistIfTruncated = false)
    {
        var tailTruncation = Truncation.TruncateTail(GetSnapshotText(), Math.Max(1, _maxLines * 4 / 5), Math.Max(1, _maxBytes * 4 / 5));
        var truncated = _totalLines > _maxLines || _totalDecodedBytes > _maxBytes;
        var truncatedBy = truncated
            ? (tailTruncation.TruncatedBy != TruncatedBy.None
                ? tailTruncation.TruncatedBy
                : (_totalDecodedBytes > _maxBytes ? TruncatedBy.Bytes : TruncatedBy.Lines))
            : TruncatedBy.None;

        var truncation = tailTruncation with
        {
            Truncated = truncated,
            TruncatedBy = truncatedBy,
            TotalLines = _totalLines,
            TotalBytes = _totalDecodedBytes,
            MaxLines = _maxLines,
            MaxBytes = _maxBytes,
        };

        if (persistIfTruncated && truncation.Truncated) EnsureTempFile();

        var content = truncated && _headText.Length > 0
            ? _headText + "\n[... omitted output ...]\n" + tailTruncation.Content
            : tailTruncation.Content;
        truncation = truncation with
        {
            Content = content,
            OutputBytes = Truncation.ByteLength(_headText) + tailTruncation.OutputBytes,
            OutputLines = Truncation.SplitLinesForCounting(_headText).Count + tailTruncation.OutputLines,
        };
        return new OutputSnapshot(content, truncation, _tempFilePath);
    }

    public void CloseTempFile()
    {
        _tempFileStream?.Dispose();
        _tempFileStream = null;
    }

    public int LastLineBytes => _currentLineBytes;

    public void Dispose() => CloseTempFile();

    private string Decode(ReadOnlySpan<byte> data, bool flush)
    {
        var charCount = _decoder.GetCharCount(data, flush);
        if (charCount == 0) return "";
        var chars = new char[charCount];
        _decoder.GetChars(data, chars, flush);
        return new string(chars);
    }

    private void AppendDecodedText(string text)
    {
        if (text.Length == 0) return;

        var bytes = Truncation.ByteLength(text);
        _totalDecodedBytes += bytes;
        if (_headBytes < _maxBytes / 5)
        {
            var remaining = _maxBytes / 5 - _headBytes;
            var low = 0;
            var high = text.Length;
            while (low < high)
            {
                var mid = low + (high - low + 1) / 2;
                if (Truncation.ByteLength(text[..mid]) <= remaining) low = mid;
                else high = mid - 1;
            }
            var chars = low;
            _headText += text[..chars];
            _headBytes += Truncation.ByteLength(text[..chars]);
        }
        _tailText += text;
        _tailBytes += bytes;
        if (_tailBytes > _maxRollingBytes * 2) TrimTail();

        var newlines = 0;
        var lastNewline = -1;
        for (var i = text.IndexOf('\n'); i >= 0; i = text.IndexOf('\n', i + 1))
        {
            newlines++;
            lastNewline = i;
        }
        if (newlines == 0)
        {
            _currentLineBytes += bytes;
            _hasOpenLine = true;
        }
        else
        {
            _completedLines += newlines;
            var tail = text[(lastNewline + 1)..];
            _currentLineBytes = Truncation.ByteLength(tail);
            _hasOpenLine = tail.Length > 0;
        }
        _totalLines = _completedLines + (_hasOpenLine ? 1 : 0);
    }

    private void TrimTail()
    {
        var buffer = Encoding.UTF8.GetBytes(_tailText);
        if (buffer.Length <= _maxRollingBytes)
        {
            _tailBytes = buffer.Length;
            return;
        }

        var start = buffer.Length - _maxRollingBytes;
        while (start < buffer.Length && (buffer[start] & 0xc0) == 0x80) start++;

        _tailStartsAtLineBoundary = start == 0 ? _tailStartsAtLineBoundary : buffer[start - 1] == 0x0a;
        _tailText = Encoding.UTF8.GetString(buffer, start, buffer.Length - start);
        _tailBytes = Truncation.ByteLength(_tailText);
    }

    private string GetSnapshotText()
    {
        if (_tailStartsAtLineBoundary) return _tailText;
        var firstNewline = _tailText.IndexOf('\n');
        return firstNewline < 0 ? _tailText : _tailText[(firstNewline + 1)..];
    }

    private bool ShouldUseTempFile() =>
        _totalRawBytes > _maxBytes || _totalDecodedBytes > _maxBytes || _totalLines > _maxLines;

    private void EnsureTempFile()
    {
        if (_tempFilePath is not null) return;
        _tempFilePath = Path.Combine(Path.GetTempPath(), $"{_tempFilePrefix}-{Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant()}.log");
        _tempFileStream = new FileStream(_tempFilePath, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        foreach (var chunk in _rawChunks) _tempFileStream.Write(chunk);
        _rawChunks.Clear();
    }
}
