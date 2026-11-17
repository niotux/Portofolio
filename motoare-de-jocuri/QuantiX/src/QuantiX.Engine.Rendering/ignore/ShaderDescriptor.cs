namespace QuantiX.Engine.Rendering_IGNORE;

/// <summary>
/// Describes how a shader program should be created.
/// Subclass to support different source formats (GLSL, SPIR-V, HLSL, etc.).
/// The renderer will throw NotSupportedException for descriptor types it doesn't handle.
/// </summary>
public abstract class ShaderDescriptor { }
