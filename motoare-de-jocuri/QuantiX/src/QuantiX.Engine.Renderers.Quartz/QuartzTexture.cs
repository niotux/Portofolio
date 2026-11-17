using GL = Silk.NET.OpenGL;
using QuantiX.Engine.Rendering;

namespace QuantiX.Engine.Renderers.Quartz;

internal sealed class QuartzTexture : ITexture
{
  private readonly GL.GL _gl;
  private readonly uint _handle;

  public uint Width { get; }
  public uint Height { get; }
  public TextureFormat Format { get; }
  public TextureMagFilter MagFilter { get; }
  public TextureMinFilter MinFilter { get; }
  public TextureWrap Wrapping { get; }

  public GPUObjectHandle ApiHandle => new() { ApiHandleValue = _handle };

  private QuartzTexture(GL.GL gl, uint handle, uint width, uint height, TextureFormat format, TextureMagFilter magFilter, TextureMinFilter minFilter, TextureWrap wrap)
  {
    _gl = gl;
    _handle = handle;
    Width = width;
    Height = height;
    Format = format;
    MagFilter = magFilter;
    MinFilter = minFilter;
    Wrapping = wrap;
  }

  internal static QuartzTexture Create1D(GL.GL gl, byte[] data, uint width, TextureFormat format, TextureMagFilter magFilter, TextureMinFilter minFilter, TextureWrap wrap)
  {
    uint handle = gl.GenTexture();
    gl.BindTexture(GL.TextureTarget.Texture1D, handle);
    SetFilterAndWrap(gl, GL.TextureTarget.Texture1D, magFilter, minFilter, wrap);
    unsafe
    {
      fixed (byte* ptr = data)
      {
        gl.TexImage1D(GL.TextureTarget.Texture1D, 0, GLEnumMapper.ToGLInternalFormat(format), width, 0, GLEnumMapper.ToGLPixelFormat(format), GLEnumMapper.ToGLPixelType(format), ptr);
      }
    }
    gl.BindTexture(GL.TextureTarget.Texture1D, 0);
    return new QuartzTexture(gl, handle, width, 1, format, magFilter, minFilter, wrap);
  }

  internal static QuartzTexture Create2D(GL.GL gl, byte[] data, uint width, uint height, TextureFormat format, TextureMagFilter magFilter, TextureMinFilter minFilter, TextureWrap wrap)
  {
    uint handle = gl.GenTexture();
    gl.BindTexture(GL.TextureTarget.Texture2D, handle);
    SetFilterAndWrap(gl, GL.TextureTarget.Texture2D, magFilter, minFilter, wrap);
    unsafe
    {
      fixed (byte* ptr = data)
      {
        gl.TexImage2D(GL.TextureTarget.Texture2D, 0, GLEnumMapper.ToGLInternalFormat(format), width, height, 0, GLEnumMapper.ToGLPixelFormat(format), GLEnumMapper.ToGLPixelType(format), ptr);
      }
    }
    gl.BindTexture(GL.TextureTarget.Texture2D, 0);
    return new QuartzTexture(gl, handle, width, height, format, magFilter, minFilter, wrap);
  }

  internal static QuartzTexture Create2DArray(GL.GL gl, byte[] data, uint width, uint height, uint layers, TextureFormat format, TextureMagFilter magFilter, TextureMinFilter minFilter, TextureWrap wrap)
  {
    uint handle = gl.GenTexture();
    gl.BindTexture(GL.TextureTarget.Texture2DArray, handle);
    SetFilterAndWrap(gl, GL.TextureTarget.Texture2DArray, magFilter, minFilter, wrap);
    unsafe
    {
      fixed (byte* ptr = data)
      {
        gl.TexImage3D(GL.TextureTarget.Texture2DArray, 0, GLEnumMapper.ToGLInternalFormat(format), width, height, layers, 0, GLEnumMapper.ToGLPixelFormat(format), GLEnumMapper.ToGLPixelType(format), ptr);
      }
    }
    gl.BindTexture(GL.TextureTarget.Texture2DArray, 0);
    return new QuartzTexture(gl, handle, width, height, format, magFilter, minFilter, wrap);
  }

  internal static QuartzTexture Create3D(GL.GL gl, byte[] data, uint width, uint height, uint depth, TextureFormat format, TextureMagFilter magFilter, TextureMinFilter minFilter, TextureWrap wrap)
  {
    uint handle = gl.GenTexture();
    gl.BindTexture(GL.TextureTarget.Texture3D, handle);
    SetFilterAndWrap(gl, GL.TextureTarget.Texture3D, magFilter, minFilter, wrap);
    unsafe
    {
      fixed (byte* ptr = data)
      {
        gl.TexImage3D(GL.TextureTarget.Texture3D, 0, GLEnumMapper.ToGLInternalFormat(format), width, height, depth, 0, GLEnumMapper.ToGLPixelFormat(format), GLEnumMapper.ToGLPixelType(format), ptr);
      }
    }
    gl.BindTexture(GL.TextureTarget.Texture3D, 0);
    return new QuartzTexture(gl, handle, width, height, format, magFilter, minFilter, wrap);
  }

  internal static QuartzTexture CreateCubeMap(GL.GL gl, byte[] face0, byte[] face1, byte[] face2, byte[] face3, byte[] face4, byte[] face5, uint width, uint height, TextureFormat format, TextureMagFilter magFilter, TextureMinFilter minFilter, TextureWrap wrap)
  {
    uint handle = gl.GenTexture();
    gl.BindTexture(GL.TextureTarget.TextureCubeMap, handle);
    SetFilterAndWrap(gl, GL.TextureTarget.TextureCubeMap, magFilter, minFilter, wrap);

    byte[][] faces = [face0, face1, face2, face3, face4, face5];
    for (int i = 0; i < 6; i++)
    {
      var target = GL.TextureTarget.TextureCubeMapPositiveX + i;
      unsafe
      {
        fixed (byte* ptr = faces[i])
        {
          gl.TexImage2D(target, 0, GLEnumMapper.ToGLInternalFormat(format), width, height, 0, GLEnumMapper.ToGLPixelFormat(format), GLEnumMapper.ToGLPixelType(format), ptr);
        }
      }
    }

    gl.BindTexture(GL.TextureTarget.TextureCubeMap, 0);
    return new QuartzTexture(gl, handle, width, height, format, magFilter, minFilter, wrap);
  }

  private static void SetFilterAndWrap(GL.GL gl, GL.TextureTarget target, TextureMagFilter magFilter, TextureMinFilter minFilter, TextureWrap wrap)
  {
    gl.TexParameter(target, GL.TextureParameterName.TextureMagFilter, (int)GLEnumMapper.ToGLMagFilter(magFilter));
    gl.TexParameter(target, GL.TextureParameterName.TextureMinFilter, (int)GLEnumMapper.ToGLMinFilter(minFilter));
    gl.TexParameter(target, GL.TextureParameterName.TextureWrapS, (int)GLEnumMapper.ToGLWrap(wrap));
    gl.TexParameter(target, GL.TextureParameterName.TextureWrapT, (int)GLEnumMapper.ToGLWrap(wrap));
    gl.TexParameter(target, GL.TextureParameterName.TextureWrapR, (int)GLEnumMapper.ToGLWrap(wrap));
  }

  public void Dispose()
  {
    _gl.DeleteTexture(_handle);
  }
}
