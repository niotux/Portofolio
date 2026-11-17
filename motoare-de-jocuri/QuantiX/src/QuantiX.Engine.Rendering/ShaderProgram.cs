using System;

namespace QuantiX.Engine.Rendering;

public readonly struct ShaderProgram
{
  public required byte[] Data { get; init; }
  public required ShaderProgramType Type { get; init; }
}
