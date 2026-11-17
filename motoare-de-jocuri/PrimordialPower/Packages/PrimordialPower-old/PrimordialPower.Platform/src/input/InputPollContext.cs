namespace PrimordialPower.Platform;

public class InputPollContext
{
  public InputPollContext(IViewport viewport)
  {

  }


  public void UpdateState()
  {

  }


  public Keyboard Keyboard { get; } = new();
  public Mouse Mouse { get; } = new();
}
