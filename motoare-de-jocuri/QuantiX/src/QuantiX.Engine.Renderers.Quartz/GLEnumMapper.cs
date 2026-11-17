using GL = Silk.NET.OpenGL;
using QuantiX.Engine.Rendering;

namespace QuantiX.Engine.Renderers.Quartz;

internal static class GLEnumMapper
{
  public static GL.InternalFormat ToGLInternalFormat(TextureFormat format) => format switch
  {
    TextureFormat.RGBA8 => GL.InternalFormat.Rgba8,
    TextureFormat.RGB8 => GL.InternalFormat.Rgb8,
    TextureFormat.R8 => GL.InternalFormat.R8,
    TextureFormat.RG8 => GL.InternalFormat.RG8,
    TextureFormat.RGBA16F => GL.InternalFormat.Rgba16f,
    TextureFormat.RGB16F => GL.InternalFormat.Rgb16f,
    TextureFormat.R16F => GL.InternalFormat.R16f,
    TextureFormat.RGBA32F => GL.InternalFormat.Rgba32f,
    TextureFormat.RGB32F => GL.InternalFormat.Rgb32f,
    TextureFormat.R32F => GL.InternalFormat.R32f,
    TextureFormat.Depth16 => GL.InternalFormat.DepthComponent16,
    TextureFormat.Depth24 => GL.InternalFormat.DepthComponent24,
    TextureFormat.Depth32F => GL.InternalFormat.DepthComponent32f,
    TextureFormat.Depth24Stencil8 => GL.InternalFormat.Depth24Stencil8,
    _ => throw new ArgumentOutOfRangeException(nameof(format), format, null)
  };

  public static GL.PixelFormat ToGLPixelFormat(TextureFormat format) => format switch
  {
    TextureFormat.RGBA8 or TextureFormat.RGBA16F or TextureFormat.RGBA32F => GL.PixelFormat.Rgba,
    TextureFormat.RGB8 or TextureFormat.RGB16F or TextureFormat.RGB32F => GL.PixelFormat.Rgb,
    TextureFormat.R8 or TextureFormat.R16F or TextureFormat.R32F => GL.PixelFormat.Red,
    TextureFormat.RG8 => GL.PixelFormat.RG,
    TextureFormat.Depth16 or TextureFormat.Depth24 or TextureFormat.Depth32F => GL.PixelFormat.DepthComponent,
    TextureFormat.Depth24Stencil8 => GL.PixelFormat.DepthStencil,
    _ => throw new ArgumentOutOfRangeException(nameof(format), format, null)
  };

  public static GL.PixelType ToGLPixelType(TextureFormat format) => format switch
  {
    TextureFormat.RGBA8 or TextureFormat.RGB8 or TextureFormat.R8 or TextureFormat.RG8 => GL.PixelType.UnsignedByte,
    TextureFormat.RGBA16F or TextureFormat.RGB16F or TextureFormat.R16F => GL.PixelType.HalfFloat,
    TextureFormat.RGBA32F or TextureFormat.RGB32F or TextureFormat.R32F or TextureFormat.Depth32F => GL.PixelType.Float,
    TextureFormat.Depth16 => GL.PixelType.UnsignedShort,
    TextureFormat.Depth24 => GL.PixelType.UnsignedInt,
    TextureFormat.Depth24Stencil8 => GL.PixelType.UnsignedInt248,
    _ => throw new ArgumentOutOfRangeException(nameof(format), format, null)
  };

  public static GL.TextureMagFilter ToGLMagFilter(TextureMagFilter filter) => filter switch
  {
    TextureMagFilter.Nearest => GL.TextureMagFilter.Nearest,
    TextureMagFilter.Linear => GL.TextureMagFilter.Linear,
    _ => throw new ArgumentOutOfRangeException(nameof(filter), filter, null)
  };

  public static GL.TextureMinFilter ToGLMinFilter(TextureMinFilter filter) => filter switch
  {
    TextureMinFilter.Nearest => GL.TextureMinFilter.Nearest,
    TextureMinFilter.Linear => GL.TextureMinFilter.Linear,
    TextureMinFilter.NearestMipmapNearest => GL.TextureMinFilter.NearestMipmapNearest,
    TextureMinFilter.NearestMipmapLinear => GL.TextureMinFilter.NearestMipmapLinear,
    TextureMinFilter.LinearMipmapNearest => GL.TextureMinFilter.LinearMipmapNearest,
    TextureMinFilter.LinearMipmapLinear => GL.TextureMinFilter.LinearMipmapLinear,
    _ => throw new ArgumentOutOfRangeException(nameof(filter), filter, null)
  };

  public static GL.TextureWrapMode ToGLWrap(TextureWrap wrap) => wrap switch
  {
    TextureWrap.Repeat => GL.TextureWrapMode.Repeat,
    TextureWrap.ClampToEdge => GL.TextureWrapMode.ClampToEdge,
    TextureWrap.MirroredRepeat => GL.TextureWrapMode.MirroredRepeat,
    TextureWrap.ClampToBorder => GL.TextureWrapMode.ClampToBorder,
    _ => throw new ArgumentOutOfRangeException(nameof(wrap), wrap, null)
  };

  public static GL.TriangleFace ToGLCullFace(CullMode mode) => mode switch
  {
    CullMode.Front => GL.TriangleFace.Front,
    CullMode.Back => GL.TriangleFace.Back,
    _ => GL.TriangleFace.FrontAndBack
  };

  public static GL.FrontFaceDirection ToGLFrontFace(FrontFace face) => face switch
  {
    FrontFace.CounterClockwise => GL.FrontFaceDirection.Ccw,
    FrontFace.Clockwise => GL.FrontFaceDirection.CW,
    _ => throw new ArgumentOutOfRangeException(nameof(face), face, null)
  };

  public static GL.PolygonMode ToGLPolygonMode(FillMode mode) => mode switch
  {
    FillMode.Solid => GL.PolygonMode.Fill,
    FillMode.Wireframe => GL.PolygonMode.Line,
    _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null)
  };

  public static GL.DepthFunction ToGLDepthFunc(DepthFunc func) => func switch
  {
    DepthFunc.Never => GL.DepthFunction.Never,
    DepthFunc.Less => GL.DepthFunction.Less,
    DepthFunc.Equal => GL.DepthFunction.Equal,
    DepthFunc.LessOrEqual => GL.DepthFunction.Lequal,
    DepthFunc.Greater => GL.DepthFunction.Greater,
    DepthFunc.NotEqual => GL.DepthFunction.Notequal,
    DepthFunc.GreaterOrEqual => GL.DepthFunction.Gequal,
    DepthFunc.Always => GL.DepthFunction.Always,
    _ => throw new ArgumentOutOfRangeException(nameof(func), func, null)
  };

  public static GL.BlendingFactor ToGLBlendFactor(BlendFactor factor) => factor switch
  {
    BlendFactor.Zero => GL.BlendingFactor.Zero,
    BlendFactor.One => GL.BlendingFactor.One,
    BlendFactor.SrcColor => GL.BlendingFactor.SrcColor,
    BlendFactor.OneMinusSrcColor => GL.BlendingFactor.OneMinusSrcColor,
    BlendFactor.DstColor => GL.BlendingFactor.DstColor,
    BlendFactor.OneMinusDstColor => GL.BlendingFactor.OneMinusDstColor,
    BlendFactor.SrcAlpha => GL.BlendingFactor.SrcAlpha,
    BlendFactor.OneMinusSrcAlpha => GL.BlendingFactor.OneMinusSrcAlpha,
    BlendFactor.DstAlpha => GL.BlendingFactor.DstAlpha,
    BlendFactor.OneMinusDstAlpha => GL.BlendingFactor.OneMinusDstAlpha,
    _ => throw new ArgumentOutOfRangeException(nameof(factor), factor, null)
  };

  public static GL.PrimitiveType ToGLPrimitiveType(PrimitiveTopology topology) => topology switch
  {
    PrimitiveTopology.Triangles => GL.PrimitiveType.Triangles,
    PrimitiveTopology.TriangleStrip => GL.PrimitiveType.TriangleStrip,
    PrimitiveTopology.TriangleFan => GL.PrimitiveType.TriangleFan,
    PrimitiveTopology.Lines => GL.PrimitiveType.Lines,
    PrimitiveTopology.LineStrip => GL.PrimitiveType.LineStrip,
    PrimitiveTopology.Points => GL.PrimitiveType.Points,
    _ => throw new ArgumentOutOfRangeException(nameof(topology), topology, null)
  };

  public static GL.ShaderType ToGLShaderType(ShaderProgramType type) => type switch
  {
    ShaderProgramType.Vertex => GL.ShaderType.VertexShader,
    ShaderProgramType.Fragment => GL.ShaderType.FragmentShader,
    ShaderProgramType.Geometry => GL.ShaderType.GeometryShader,
    ShaderProgramType.Tesselation => GL.ShaderType.TessControlShader,
    ShaderProgramType.Compute => GL.ShaderType.ComputeShader,
    _ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
  };
}
