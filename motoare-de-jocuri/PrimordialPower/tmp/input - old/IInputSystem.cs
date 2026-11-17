namespace PrimordialPower.Core;

public interface IInputSystem : ITaskComponent
{
  InputContext CreateInputContext(IViewport viewportContext);
}
