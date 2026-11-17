using System.Text;

namespace UnivKit.Application;

public interface IService
{
  Task Execute(CancellationToken cancellationToken);
}

public static class HostedServiceExtensions
{
  public static string GetInstanceId<TService>(this TService service)
    where TService : IService
  {
    return $"{service.GetType().FullName}::hash_{service.GetHashCode()}";
  }

  public static Task Launch(this IService host, CancellationToken cancellationToken = default)
  {
    return Task.Run(() => host.Execute(cancellationToken));
  }
}