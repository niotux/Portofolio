using System;
using PrimordialPower.Runtime;

namespace PrimordialPower.Platform;

public class AudioServer : Server<IAudioInterface>
{
  public override IAudioInterface Services { get; }
}
