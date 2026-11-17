using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using PrimordialPower.Platform;
using PrimordialPower.Runtime;

namespace PrimordialPower.Renderers.Obsidian;

public partial class Renderer : IDisposable
{
  static Renderer()
  {
    NativeLibrary.SetDllImportResolver(Assembly.GetExecutingAssembly(), LibResolver.DllImportResolver);
    Instance = new Renderer();
  }



  [LibraryImport(LibNames.Obsidian)]
  private static partial IntPtr Renderer_New();

  [LibraryImport(LibNames.Obsidian)]
  private static partial void Renderer_Dispose(IntPtr handle);

  [LibraryImport(LibNames.Obsidian, StringMarshalling = StringMarshalling.Utf8)]
  private static partial void Renderer_Init(IntPtr handle,
    uint appVersion, uint engineVersion, VkApiVersion vkApiVersion,
    string appName, string engineName);

  [DllImport(LibNames.Obsidian)]
  private static extern IntPtr Renderer_CreateContext(IntPtr handle, ref NativeViewportData viewport);

  [LibraryImport(LibNames.Obsidian)]
  internal static partial void RendererContext_DrawFrame(IntPtr handle);

  [LibraryImport(LibNames.Obsidian)]
  internal static partial void RendererContext_RecreateSwapchain(IntPtr handle, uint width, uint height);

  [LibraryImport(LibNames.Obsidian)]
  internal static partial void RendererContext_Dispose(IntPtr handle);



  [StructLayout(LayoutKind.Sequential)]
  private struct NativeViewportData
  {
    public uint Width;
    public uint Height;
    public int OffsetX;
    public int OffsetY;
    public ViewportHandle Handle;
  }



  public static Renderer Instance { get; }
  private IntPtr handle;

  private Renderer()
  {
    this.handle = Renderer_New();
  }

  public void Init(ApplicationData app)
  {
    Renderer_Init(
      this.handle,
      app.AppVersion,
      app.EngineVersion,
      VkApiVersion.Vk1_0,
      app.AppName,
      app.EngineName
    );
  }

  public RendererContext CreateContext(IViewport viewport)
  {
    var nativeViewport = new NativeViewportData
    {
      Width = viewport.Size.Width,
      Height = viewport.Size.Height,
      OffsetX = viewport.Offset.X,
      OffsetY = viewport.Offset.Y,
      Handle = viewport.Handle,
    };

    IntPtr nativeContext = Renderer_CreateContext(this.handle, ref nativeViewport);
    return new RendererContext(this, viewport, nativeContext);
  }



  ~Renderer()
  {
    DisposeImpl();
  }

  public void Dispose()
  {
    DisposeImpl();
    GC.SuppressFinalize(this);
  }

  private void DisposeImpl()
  {
    IntPtr inst = Interlocked.Exchange(ref this.handle, 0);
    if (inst == 0) return;

    Renderer_Dispose(inst);
  }



  private static class LibNames
  {
    public const string Obsidian = "obsidian";
  }

  private static class LibResolver
  {
    public static IntPtr DllImportResolver(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
      switch (libraryName)
      {
        case LibNames.Obsidian:
          return NativeLibrary.Load(_obsidian);
      }
      return 0;
    }

    public static string RID { get; } = GetRID();
    private static readonly string _obsidian = $"runtimes/{RID}/native/{GetLibName("obsidian")}";
    private static string GetRID()
    {
      string os =
      OperatingSystem.IsWindows() ? "win" :
      OperatingSystem.IsLinux() ? "linux" :
      OperatingSystem.IsMacOS() ? "macos" :
      throw new Exception("Unknown OS platform");

      string arch = RuntimeInformation.OSArchitecture switch
      {
        Architecture.X64 => "x64",
        Architecture.X86 => "x86",
        Architecture.Arm64 => "arm64",
        Architecture.Arm => "arm",
        _ => throw new Exception("Unknown CPU architecture"),
      };

      return $"{os}-{arch}";
    }

    private static string GetLibName(string name)
    {
      string result =
      OperatingSystem.IsWindows() ? $"{name}.dll" :
      OperatingSystem.IsLinux() ? $"lib{name}.so" :
      OperatingSystem.IsMacOS() ? $"lib{name}.dylib" :
      throw new Exception("Unknown OS platform");

      return result;
    }
  }
}
