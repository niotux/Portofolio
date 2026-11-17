using GL = Silk.NET.OpenGL;
using QuantiX.Engine.Rendering;

namespace QuantiX.Engine.Renderers.Quartz;

internal static class QuartzPipelineDrawer
{
  internal static void Draw(GL.GL gl, uint vao, uint fbo, uint width, uint height, Pipeline[] pipelines)
  {
    gl.BindFramebuffer(GL.FramebufferTarget.Framebuffer, fbo);
    gl.Viewport(0, 0, width, height);

    gl.DepthMask(true);
    gl.Clear(GL.ClearBufferMask.ColorBufferBit | GL.ClearBufferMask.DepthBufferBit | GL.ClearBufferMask.StencilBufferBit);
    gl.BindVertexArray(vao);

    foreach (var pipeline in pipelines)
    {
      ApplyPipelineState(gl, pipeline.State);
      gl.UseProgram((uint)pipeline.ShaderApiHandle.ApiHandleValue);

      foreach (var entry in pipeline.DrawEntries)
      {
        for (int i = 0; i < entry.BoundTextures.Length; i++)
        {
          gl.ActiveTexture(GL.TextureUnit.Texture0 + i);
          gl.BindTexture(GL.TextureTarget.Texture2D, (uint)entry.BoundTextures[i].ApiHandleValue);
        }

        gl.BindBuffer(GL.BufferTargetARB.ArrayBuffer, (uint)entry.VertexBuffer.ApiHandleValue);
        for (uint a = 0; a < pipeline.VertexAttributes.Length; a++)
        {
          var attr = pipeline.VertexAttributes[a];
          gl.EnableVertexAttribArray(attr.Location);
          unsafe
          {
            gl.VertexAttribPointer(attr.Location, (int)attr.Size, GL.VertexAttribPointerType.Float, false, attr.Stride, (void*)(nuint)attr.Offset);
          }
        }

        gl.BindBuffer(GL.BufferTargetARB.ElementArrayBuffer, (uint)entry.IndexBuffer.ApiHandleValue);
        gl.BindBufferRange(GL.BufferTargetARB.UniformBuffer, 0, (uint)entry.UniformBuffer.ApiHandleValue, (nint)entry.UniformStart, entry.UniformSize);

        var primitiveType = GLEnumMapper.ToGLPrimitiveType(pipeline.State.Topology);
        unsafe
        {
          gl.DrawElementsBaseVertex(primitiveType, entry.IndexCount, GL.DrawElementsType.UnsignedInt, (void*)(nuint)(entry.IndexStart * sizeof(uint)), entry.VertexOffset);
        }
      }
    }

    gl.BindVertexArray(0);
    gl.BindFramebuffer(GL.FramebufferTarget.Framebuffer, 0);
  }

  private static void ApplyPipelineState(GL.GL gl, PipelineState state)
  {
    if (state.CullMode == CullMode.None)
    {
      gl.Disable(GL.EnableCap.CullFace);
    }
    else
    {
      gl.Enable(GL.EnableCap.CullFace);
      gl.CullFace(GLEnumMapper.ToGLCullFace(state.CullMode));
    }

    gl.FrontFace(GLEnumMapper.ToGLFrontFace(state.FrontFace));
    gl.PolygonMode(GL.TriangleFace.FrontAndBack, GLEnumMapper.ToGLPolygonMode(state.FillMode));

    gl.Enable(GL.EnableCap.DepthTest);
    gl.DepthFunc(GLEnumMapper.ToGLDepthFunc(state.DepthFunc));
    gl.DepthMask(state.DepthWrite);

    if (state.DepthBiasConstant != 0 || state.DepthBiasSlope != 0)
    {
      gl.Enable(GL.EnableCap.PolygonOffsetFill);
      gl.PolygonOffset(state.DepthBiasSlope, state.DepthBiasConstant);
    }
    else
    {
      gl.Disable(GL.EnableCap.PolygonOffsetFill);
    }

    if (state.SrcBlendFactor == BlendFactor.One && state.DstBlendFactor == BlendFactor.Zero)
    {
      gl.Disable(GL.EnableCap.Blend);
    }
    else
    {
      gl.Enable(GL.EnableCap.Blend);
      gl.BlendFunc(GLEnumMapper.ToGLBlendFactor(state.SrcBlendFactor), GLEnumMapper.ToGLBlendFactor(state.DstBlendFactor));
    }
  }
}
