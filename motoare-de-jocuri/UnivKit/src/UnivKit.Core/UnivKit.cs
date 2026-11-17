using System.Reflection;
using Microsoft.Extensions.Logging;

namespace UnivKit.Core;

public static class UnivKit
{
  public static ILogger Logger { get; }
  public static Version Version { get; } = Assembly.GetExecutingAssembly().GetName().Version ?? new(0, 0, 0);
}
