using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using PrimordialPower.Runtime;

namespace PrimordialPower.Platform.Win32;

[SupportedOSPlatform("windows")]
internal class Win32AudioInterfaceServer : Server
{
  private API.COM.IMMDeviceEnumerator? deviceEnumerator;
  private API.COM.IMMDevice? device;
  private API.COM.IAudioClient? audioClient;
  private API.COM.IAudioRenderClient? renderClient;

  private IntPtr eventHandle;
  private AudioFormat audioFormat;



  public override ServerType Type => ServerType.MonoThread;
  public override void Init()
  {


  }
}