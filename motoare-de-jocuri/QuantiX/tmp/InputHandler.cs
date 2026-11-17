using Oblivion.Engine.Input;

namespace FPSGame;

public class InputHandler
{
  private readonly Dictionary<string, float> _axisValues = new();
  private readonly HashSet<Key> _pressedKeys = new();
  private readonly HashSet<MouseButton> _pressedMouseButtons = new();

  public InputHandler()
  {
    // Initialize axis values
    _axisValues["Mouse X"] = 0f;
    _axisValues["Mouse Y"] = 0f;
    _axisValues["Mouse Wheel"] = 0f;
    _axisValues["Horizontal"] = 0f;
    _axisValues["Vertical"] = 0f;
  }

  public void Update(float deltaTime)
  {
    // Reset axis values
    _axisValues["Mouse X"] = 0f;
    _axisValues["Mouse Y"] = 0f;
    _axisValues["Mouse Wheel"] = 0f;

    // Get mouse movement
    _axisValues["Mouse X"] = InputSystem.GetMouseDeltaX();
    _axisValues["Mouse Y"] = InputSystem.GetMouseDeltaY();
    _axisValues["Mouse Wheel"] = InputSystem.GetMouseWheelDelta();

    // Get keyboard input for axes
    float horizontal = 0f;
    float vertical = 0f;

    if (IsKeyDown(Key.D) || IsKeyDown(Key.RightArrow))
      horizontal += 1f;
    if (IsKeyDown(Key.A) || IsKeyDown(Key.LeftArrow))
      horizontal -= 1f;
    if (IsKeyDown(Key.W) || IsKeyDown(Key.UpArrow))
      vertical += 1f;
    if (IsKeyDown(Key.S) || IsKeyDown(Key.DownArrow))
      vertical -= 1f;

    _axisValues["Horizontal"] = horizontal;
    _axisValues["Vertical"] = vertical;
  }

  public bool IsKeyDown(Key key)
  {
    return InputSystem.IsKeyDown(key);
  }

  public bool IsMouseButtonDown(MouseButton button)
  {
    return InputSystem.IsMouseButtonDown(button);
  }

  public float GetAxis(string axisName)
  {
    return _axisValues.TryGetValue(axisName, out float value) ? value : 0f;
  }

  public Vector2 GetMousePosition()
  {
    return InputSystem.GetMousePosition();
  }

  public void SetCursorVisible(bool visible)
  {
    InputSystem.SetCursorVisible(visible);
  }

  public void SetCursorLocked(bool locked)
  {
    InputSystem.SetCursorLocked(locked);
  }
}