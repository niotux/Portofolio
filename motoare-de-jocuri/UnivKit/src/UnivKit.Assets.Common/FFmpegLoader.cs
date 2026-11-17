using System.Runtime.InteropServices;
using FFmpeg.AutoGen;

namespace UnivKit.Audio.FFmpeg;

internal static class FFmpegLoader
{
  private static readonly Lock _gate = new();
  private static bool _initialized;

  public static void EnsureInitialized()
  {
    if (_initialized)
    {
      return;
    }
    lock (_gate)
    {
      if (_initialized)
      {
        return;
      }

      string baseDir = AppContext.BaseDirectory;
      string rid = ResolveRid();
      string ridNative = Path.Combine(baseDir, "runtimes", rid, "native");

      ffmpeg.RootPath = Directory.Exists(ridNative)
        ? ridNative
        : baseDir;

      DynamicallyLoadedBindings.Initialize();
      _initialized = true;
      ffmpeg.av_log_set_level(ffmpeg.AV_LOG_QUIET);
    }
  }

  private static string ResolveRid()
  {
    string arch = RuntimeInformation.ProcessArchitecture switch
    {
      Architecture.X64 => "x64",
      Architecture.X86 => "x86",
      Architecture.Arm64 => "arm64",
      Architecture.Arm => "arm",
      _ => throw new PlatformNotSupportedException($"Unsupported architecture: {RuntimeInformation.ProcessArchitecture}")
    };

    if (OperatingSystem.IsWindows())
    {
      return $"win-{arch}";
    }
    if (OperatingSystem.IsMacOS())
    {
      return $"osx-{arch}";
    }
    if (OperatingSystem.IsLinux())
    {
      return $"linux-{arch}";
    }
    throw new PlatformNotSupportedException("Unsupported OS for FFmpeg native loading.");
  }
}
