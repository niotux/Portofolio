using System;
using System.Runtime.InteropServices;

namespace PrimordialPower.Platform.Win32;

internal static partial class API
{
  [LibraryImport(Lib.AVRT, SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
  public static partial IntPtr AvSetMmThreadCharacteristicsW(string TaskName, out uint TaskIndex);


  [LibraryImport(Lib.AVRT, SetLastError = true)]
  [return: MarshalAs(UnmanagedType.Bool)]
  public static partial bool AvRevertMmThreadCharacteristics(IntPtr AvrtHandle);




  public static class COMAudioGuids
  {
    public static readonly Guid CLSID_MMDeviceEnumerator
    = new("BCDE0395-E52F-467C-8E3D-C4579291692E");

    public static readonly Guid IID_IMMDeviceEnumerator
    = new("A95664D2-9614-4F35-A746-DE8DB63617E6");

    public static readonly Guid IID_IMMDevice
    = new("D666063F-1587-4E43-81F1-B948E807363F");

    public static readonly Guid IID_IAudioClient
    = new("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2");

    public static readonly Guid IID_IAudioRenderClient
    = new("F294ACFC-3146-4483-A7BF-ADDCA7C260E2");

    public static readonly Guid IID_IAudioCaptureClient
    = new("C8ADBD64-E71E-48a0-A4DE-185C395CD317");

    public static readonly Guid IID_IAudioEndpointVolume
    = new("5CDF2C82-841E-4546-9722-0CF74078229A");

    public static readonly Guid IID_IAudioMeterInformation
    = new("C02216F6-8C67-4b5b-9D00-D008E73E0064");

    public static readonly Guid IID_IAudioSessionManager2
    = new("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F");

    public static readonly Guid IID_IAudioSessionEnumerator
    = new("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8");

    public static readonly Guid IID_IAudioSessionControl
    = new("F4B1A599-7266-4319-A8CA-E70ACB11E8CD");

    public static readonly Guid IID_IPropertyStore
    = new("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99");
  }



  public static partial class COM
  {
    [ComImport]
    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMMDeviceEnumerator
    {
      int EnumAudioEndpoints(EDataFlow dataFlow, DeviceState stateMask, out IMMDeviceCollection devices);
      int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice device);
      int GetDevice(string id, out IMMDevice device);
      int RegisterEndpointNotificationCallback(IMMNotificationClient client);
      int UnregisterEndpointNotificationCallback(IMMNotificationClient client);
    }

    [ComImport]
    [Guid("D666063F-1587-4E43-81F1-B948E807363F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMMDevice
    {
      int Activate(ref Guid iid, CLSCTX dwClsCtx, IntPtr pActivationParams, out IntPtr ppInterface);
      int OpenPropertyStore(StorageAccessMode accessMode, out IPropertyStore properties);
      int GetId(out string id);
      int GetState(out DeviceState state);
    }

    [ComImport]
    [Guid("0BD7A1BE-7A1A-44DB-8397-C0EE05F1EE3E")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMMDeviceCollection
    {
      int GetCount(out uint count);
      int Item(uint index, out IMMDevice device);
    }

    [ComImport]
    [Guid("7991EEC9-7E89-4D85-8390-6C703CEC60C0")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMMNotificationClient
    {
      void OnDeviceStateChanged(string deviceId, DeviceState newState);
      void OnDeviceAdded(string deviceId);
      void OnDeviceRemoved(string deviceId);
      void OnDefaultDeviceChanged(EDataFlow flow, ERole role, string deviceId);
      void OnPropertyValueChanged(string deviceId, PropertyKey key);
    }

    [ComImport]
    [Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IAudioClient
    {
      int Initialize(
        AudioClientShareMode shareMode,
        AudioClientStreamFlags streamFlags,
        long hnsBufferDuration,
        long hnsPeriodicity,
        ref WaveFormat pFormat,
        IntPtr audioSessionGuid
      );

      int GetBufferSize(out uint numBufferFrames);
      int GetStreamLatency(out long latency);
      int GetCurrentPadding(out uint numPaddingFrames);
      int IsFormatSupported(
        AudioClientShareMode shareMode,
        ref WaveFormat pFormat,
        out IntPtr ppClosestMatch
      );
      int GetMixFormat(out IntPtr ppFormat);
      int GetDevicePeriod(out long defaultPeriod, out long minimumPeriod);
      int Start();
      int Stop();
      int Reset();
      int SetEventHandle(IntPtr eventHandle);
      int GetService(ref Guid riid, out IntPtr ppv);
    }

    [ComImport]
    [Guid("F294ACFC-3146-4483-A7BF-ADDCA7C260E2")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IAudioRenderClient
    {
      int GetBuffer(uint numFramesRequested, out IntPtr data);
      int ReleaseBuffer(uint numFramesWritten, AudioClientBufferFlags flags);
    }

    [ComImport]
    [Guid("C8ADBD64-E71E-48a0-A4DE-185C395CD317")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IAudioCaptureClient
    {
      int GetBuffer(
        out IntPtr data,
        out uint numFramesToRead,
        out AudioClientBufferFlags flags,
        out ulong devicePosition,
        out ulong qpcPosition
      );

      int ReleaseBuffer(uint numFramesRead);
      int GetNextPacketSize(out uint numFramesInNextPacket);
    }

    [ComImport]
    [Guid("5CDF2C82-841E-4546-9722-0CF74078229A")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IAudioEndpointVolume
    {
      int RegisterControlChangeNotify(IAudioEndpointVolumeCallback notify);
      int UnregisterControlChangeNotify(IAudioEndpointVolumeCallback notify);
      int GetChannelCount(out uint count);

      int SetMasterVolumeLevel(float levelDB, Guid eventContext);
      int SetMasterVolumeLevelScalar(float level, Guid eventContext);
      int GetMasterVolumeLevel(out float levelDB);
      int GetMasterVolumeLevelScalar(out float level);

      int SetChannelVolumeLevel(uint channel, float levelDB, Guid eventContext);
      int SetChannelVolumeLevelScalar(uint channel, float level, Guid eventContext);
      int GetChannelVolumeLevel(uint channel, out float levelDB);
      int GetChannelVolumeLevelScalar(uint channel, out float level);

      int SetMute(bool mute, Guid eventContext);
      int GetMute(out bool mute);

      int GetVolumeStepInfo(out uint step, out uint stepCount);
      int VolumeStepUp(Guid eventContext);
      int VolumeStepDown(Guid eventContext);

      int QueryHardwareSupport(out uint hwSupportMask);
      int GetVolumeRange(out float minDB, out float maxDB, out float incrementDB);
    }

    [ComImport]
    [Guid("657804FA-D6AD-4496-8A60-352752AF4F89")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IAudioEndpointVolumeCallback
    {
      void OnNotify(IntPtr notifyData);
    }

    [ComImport]
    [Guid("C02216F6-8C67-4b5b-9D00-D008E73E0064")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IAudioMeterInformation
    {
      int GetPeakValue(out float peakValue);
      int GetMeteringChannelCount(out uint count);
      int GetChannelsPeakValues(uint count, [Out] float[] peakValues);
    }

    [ComImport]
    [Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IAudioSessionManager2
    {
      int GetAudioSessionControl(ref Guid sessionGuid, uint streamFlags, out IAudioSessionControl sessionControl);
      int GetSimpleAudioVolume(ref Guid sessionGuid, uint streamFlags, out ISimpleAudioVolume audioVolume);
      int GetSessionEnumerator(out IAudioSessionEnumerator sessionEnum);
    }

    [ComImport]
    [Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IAudioSessionEnumerator
    {
      int GetCount(out int sessionCount);
      int GetSession(int index, out IAudioSessionControl session);
    }

    [ComImport]
    [Guid("F4B1A599-7266-4319-A8CA-E70ACB11E8CD")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IAudioSessionControl
    {
      int GetState(out AudioSessionState state);
      int GetDisplayName(out string name);
      int SetDisplayName(string value, ref Guid eventContext);
      int GetIconPath(out string path);
      int SetIconPath(string value, ref Guid eventContext);
      int GetGroupingParam(out Guid groupingId);
      int SetGroupingParam(ref Guid groupingId, ref Guid eventContext);
      int RegisterAudioSessionNotification(IAudioSessionEvents client);
      int UnregisterAudioSessionNotification(IAudioSessionEvents client);
    }

    [ComImport]
    [Guid("bfb7ff88-7239-4fc9-8fa2-07c950be9c6d")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IAudioSessionEvents
    {
      void OnDisplayNameChanged(string newName, ref Guid eventContext);
      void OnIconPathChanged(string newIconPath, ref Guid eventContext);
      void OnSimpleVolumeChanged(float newVolume, bool newMute, ref Guid eventContext);
      void OnChannelVolumeChanged(uint channelCount, IntPtr newChannelVolumeArray, uint changedChannel, ref Guid eventContext);
      void OnGroupingParamChanged(ref Guid newGroupingParam, ref Guid eventContext);
      void OnStateChanged(AudioSessionState newState);
      void OnSessionDisconnected(AudioSessionDisconnectReason disconnectReason);
    }

    [ComImport]
    [Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface ISimpleAudioVolume
    {
      int SetMasterVolume(float volume, IntPtr eventContext);
      int GetMasterVolume(out float volume);
      int SetMute(bool mute, IntPtr eventContext);
      int GetMute(out bool mute);
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PropertyKey
    {
      public Guid fmtid;
      public uint pid;
    }

    public enum StorageAccessMode : uint
    {
      Read = 0x00000000,
      Write = 0x00000001,
      ReadWrite = 0x00000002
    }

    [ComImport]
    [Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IPropertyStore
    {
      int GetCount(out uint count);
      int GetAt(uint index, out PropertyKey key);
      int GetValue(ref PropertyKey key, out PropVariant value);
      int SetValue(ref PropertyKey key, ref PropVariant value);
      int Commit();
    }
  }

  [StructLayout(LayoutKind.Sequential)]
  public struct PropVariant
  {
    public ushort vt;
    public ushort wReserved1;
    public ushort wReserved2;
    public ushort wReserved3;
    public IntPtr data1;
    public IntPtr data2;
  }



  [Flags]
  public enum CLSCTX : uint
  {
    INPROC_SERVER = 0x1,
    INPROC_HANDLER = 0x2,
    LOCAL_SERVER = 0x4,
    INPROC_SERVER16 = 0x8,
    REMOTE_SERVER = 0x10,
    INPROC_HANDLER16 = 0x20,
    RESERVED1 = 0x40,
    RESERVED2 = 0x80,
    RESERVED3 = 0x100,
    RESERVED4 = 0x200,
    NO_CODE_DOWNLOAD = 0x400,
    RESERVED5 = 0x800,
    NO_CUSTOM_MARSHAL = 0x1000,
    ENABLE_CODE_DOWNLOAD = 0x2000,
    NO_FAILURE_LOG = 0x4000,
    DISABLE_AAA = 0x8000,
    ENABLE_AAA = 0x10000,
    FROM_DEFAULT_CONTEXT = 0x20000,
    ACTIVATE_32_BIT_SERVER = 0x40000,
    ACTIVATE_64_BIT_SERVER = 0x80000,

    SERVER = INPROC_SERVER | LOCAL_SERVER | REMOTE_SERVER,
    ALL = SERVER | INPROC_HANDLER
  }

  public enum EDataFlow
  {
    Render,
    Capture,
    All
  }

  public enum ERole
  {
    Console,
    Multimedia,
    Communications
  }

  [Flags]
  public enum DeviceState : uint
  {
    Active = 1,
    Disabled = 2,
    NotPresent = 4,
    Unplugged = 8,
    All = 0xF
  }

  public enum AudioClientShareMode
  {
    Shared = 0,
    Exclusive = 1
  }

  [Flags]
  public enum AudioClientStreamFlags
  {
    None = 0,
    Loopback = 0x00020000,
    EventCallback = 0x00040000,
  }

  [Flags]
  public enum AudioClientBufferFlags
  {
    None = 0,
    DataDiscontinuity = 0x1,
    Silent = 0x2,
    TimestampError = 0x4
  }

  public enum AudioSessionState
  {
    Inactive = 0,
    Active = 1,
    Expired = 2
  }

  public enum AudioSessionDisconnectReason
  {
    DeviceRemoval = 0,
    ServerShutdown = 1,
    FormatChanged = 2,
    SessionLogoff = 3,
    SessionDisconnected = 4,
    ExclusiveModeOverride = 5
  }

  [StructLayout(LayoutKind.Sequential)]
  public struct WaveFormat
  {
    public ushort formatTag;
    public ushort channels;
    public uint samplesPerSec;
    public uint avgBytesPerSec;
    public ushort blockAlign;
    public ushort bitsPerSample;
    public ushort size;
  }
}
