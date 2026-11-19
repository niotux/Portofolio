using System.Text;

namespace PiSharp.Tools;

/// <summary>A bounded session log with monotonic produced/delivered positions, independent of display snapshots.</summary>
public sealed class SessionOutput(int capacity = 256 * 1024)
{
    private readonly object _gate = new();
    private readonly StringBuilder _text = new();
    private long _produced, _delivered;

    public void Append(string text)
    {
        lock (_gate)
        {
            _text.Append(text);
            _produced += text.Length;
            if (_text.Length > capacity)
            {
                var remove = _text.Length - capacity;
                if (remove < _text.Length && char.IsLowSurrogate(_text[remove])) remove++;
                _text.Remove(0, remove);
            }
        }
    }

    public bool HasUnread { get { lock (_gate) return _produced > _delivered; } }

    public string Read(int maxCharacters = int.MaxValue)
    {
        if (maxCharacters < 2) throw new ArgumentOutOfRangeException(nameof(maxCharacters));
        lock (_gate)
        {
            var start = _produced - _text.Length;
            var dropped = Math.Max(0, start - _delivered);
            var offset = (int)Math.Max(0, _delivered - start);
            var count = Math.Min(maxCharacters, _text.Length - offset);
            if (count > 0 && char.IsHighSurrogate(_text[offset + count - 1])) count--;
            var text = _text.ToString(offset, count);
            _delivered = start + offset + count;
            return (dropped > 0 ? $"[session retention: {dropped} unread characters dropped]\n" : "") + text;
        }
    }
}
