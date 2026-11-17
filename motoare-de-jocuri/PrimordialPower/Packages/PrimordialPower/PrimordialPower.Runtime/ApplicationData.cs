using System;

namespace PrimordialPower.Runtime;

public readonly struct ApplicationData
{
  public required uint AppVersion { get; init; }
  public required uint EngineVersion { get; init; }
  public required string AppName { get; init; }
  public required string EngineName { get; init; }
}