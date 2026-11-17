namespace QuantiX.Engine.Tests.FPS;

/// <summary>
/// Simulates held-key state from discrete console keypresses.
/// Each keypress resets a hold timer; keys with active timers are "held".
/// </summary>
public class InputState
{
  // How long a key stays "held" after a single press (simulates key repeat)
  private const float HoldDuration = 0.18f;

  private readonly Dictionary<ConsoleKey, float> _timers = new();

  /// <summary>Call for each keypress event.</summary>
  public void KeyPressed(ConsoleKey key)
  {
    _timers[key] = HoldDuration;
  }

  /// <summary>Call once per frame to decay timers.</summary>
  public void Update(float dt)
  {
    var expired = new List<ConsoleKey>();
    foreach (var (key, _) in _timers)
    {
      _timers[key] -= dt;
      if (_timers[key] <= 0)
        expired.Add(key);
    }
    foreach (var key in expired)
      _timers.Remove(key);
  }

  /// <summary>Is this key currently "held"?</summary>
  public bool IsHeld(ConsoleKey key) => _timers.ContainsKey(key);

  /// <summary>Check and consume a single press (for one-shot actions like shoot/jump).</summary>
  public bool ConsumePress(ConsoleKey key)
  {
    if (_timers.ContainsKey(key))
    {
      _timers.Remove(key);
      return true;
    }
    return false;
  }
}
