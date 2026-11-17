namespace QuantiX.Engine.Rendering_IGNORE;

/// <summary>
/// Shader descriptor for SPIR-V bytecode.
/// Supported by Vulkan backends and OpenGL 4.6+.
/// Load bytecode with File.ReadAllBytes("shader.vert.spv") etc.
/// </summary>
public sealed class SpirVShaderDescriptor : ShaderDescriptor
{
  public SpirVShaderDescriptor(params SpirVStage[] stages)
  {
    Stages = stages;
  }

  public IReadOnlyList<SpirVStage> Stages { get; }
}

/// <summary>A single SPIR-V shader stage: the stage type and its compiled bytecode.</summary>
public readonly record struct SpirVStage(ShaderType Type, ReadOnlyMemory<byte> Bytecode);
