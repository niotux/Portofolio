using GL = Silk.NET.OpenGL;
using QuantiX.Engine.Rendering;

namespace QuantiX.Engine.Renderers.Quartz;

internal sealed class QuartzShader : IShader
{
  private const uint SPIRV_MAGIC = 0x07230203;

  private readonly GL.GL _gl;
  private readonly uint _programHandle;

  public ShaderProgram[] Programs { get; }
  public GPUObjectHandle ApiHandle => new() { ApiHandleValue = _programHandle };

  internal QuartzShader(GL.GL gl, ShaderProgram[] programs)
  {
    _gl = gl;
    Programs = programs;

    _programHandle = _gl.CreateProgram();
    Span<uint> shaderHandles = stackalloc uint[programs.Length];

    for (int i = 0; i < programs.Length; i++)
    {
      var program = programs[i];
      var shaderType = GLEnumMapper.ToGLShaderType(program.Type);
      uint shader = _gl.CreateShader(shaderType);

      if (IsSpirV(program.Data))
      {
        CompileSpirV(shader, program);
      }
      else
      {
        CompileGLSL(shader, program);
      }

      int status = 0;
      _gl.GetShader(shader, GL.ShaderParameterName.CompileStatus, out status);
      if (status == 0)
      {
        string log = _gl.GetShaderInfoLog(shader);
        _gl.DeleteShader(shader);
        throw new InvalidOperationException($"Shader compilation failed ({program.Type}): {log}");
      }

      _gl.AttachShader(_programHandle, shader);
      shaderHandles[i] = shader;
    }

    _gl.LinkProgram(_programHandle);

    int linkStatus = 0;
    _gl.GetProgram(_programHandle, GL.ProgramPropertyARB.LinkStatus, out linkStatus);
    if (linkStatus == 0)
    {
      string log = _gl.GetProgramInfoLog(_programHandle);
      throw new InvalidOperationException($"Shader program linking failed: {log}");
    }

    for (int i = 0; i < shaderHandles.Length; i++)
    {
      _gl.DetachShader(_programHandle, shaderHandles[i]);
      _gl.DeleteShader(shaderHandles[i]);
    }

  }

  private static bool IsSpirV(byte[] data)
  {
    if (data.Length < 4) return false;
    uint magic = BitConverter.ToUInt32(data, 0);
    return magic == SPIRV_MAGIC;
  }

  private void CompileSpirV(uint shader, ShaderProgram program)
  {
    unsafe
    {
      fixed (byte* ptr = program.Data)
      {
        uint s = shader;
        _gl.ShaderBinary(1, &s, (GL.GLEnum)0x9551 /* GL_SHADER_BINARY_FORMAT_SPIR_V */, ptr, (uint)program.Data.Length);
      }
      _gl.SpecializeShader(shader, "main", 0, null, null);
    }
  }

  private void CompileGLSL(uint shader, ShaderProgram program)
  {
    string source = System.Text.Encoding.UTF8.GetString(program.Data);
    _gl.ShaderSource(shader, source);
    _gl.CompileShader(shader);
  }

  public void Dispose()
  {
    _gl.DeleteProgram(_programHandle);
  }
}
