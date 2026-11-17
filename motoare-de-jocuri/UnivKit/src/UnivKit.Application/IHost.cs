namespace UnivKit.Application;

public interface IHost
{
  void Add(IService service);
  void StartAll();
  Task StopAll();
  Task WaitForStop();
}