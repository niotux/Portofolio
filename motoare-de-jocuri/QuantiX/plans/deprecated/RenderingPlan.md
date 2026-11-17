# Rendering System Plan

## Core Concepts

### The Central Component (`IRenderer`)
The **central hub** — bonds with the graphics API, manages the GL context and all GPU resources. It is NOT the thing you draw to. Shared across the application.

### The Viewport
A **drawable target** bound to a single `Surface`. Obtained from the central component. Holds swap-chain info and presents to its surface. Viewports are independent — designed to be drawn to from separate threads (multi-context via shared GL contexts; initial implementation is single-thread, multi-context is the upgrade path).

### Renderable Modules
The **GPU portal** — a stateful grouping of everything needed to issue a set of draws: a compiled shader, vertex attribute layout, geometry pools, and a per-frame draw list. No dimensionality is hardcoded — all attribute layout is defined per-module, per-shader.

---

## Key Abstractions (`Name.Engine.Rendering`)

| Interface/Type | Responsibility |
|---|---|
| `IRenderer` | Central hub: resource factory, module factory, frame lifecycle. |
| `IViewport` | Surface-bound drawable target. Holds size + presents. |
| `IRenderableModule` | Pipeline + geometry pools + draw list. Core drawing unit. |
| `IDrawHandle` | Opaque reference to a static geometry allocation in a module. Disposable. |
| `IShader` | Compiled shader program handle. Disposable. |
| `IGpuTexture` | GPU texture handle. Disposable. |
| `RenderableModuleDescriptor` | Data-only descriptor for creating a module. |
| `VertexLayout` | Vertex attribute descriptors (location, base type, component count, offset, stride). |
| `PipelineState` | Blend + depth + rasterizer + topology — all combined. |

> The old `IDrawCommand`/`IViewport.Draw` model is **removed**. Drawing goes through `IRenderableModule.DrawStatic`/`DrawDynamic`.

---

## Geometry Pools

Each module owns two pools. No per-object buffers.

**Static Pool** — persistent geometry
- One VBO + one IBO shared by all static objects in the module
- Bump allocator with a free list for holes
- `AllocateStatic(vertices, indices) → IDrawHandle` — uploads once, returns handle
- Handle disposed → region marked free
- Geometry never re-uploaded

**Dynamic Pool** — per-frame transient geometry
- Ring-buffer VBO + IBO, reset each `BeginFrame`
- Caller re-submits geometry every frame
- Immediate upload on submit

**Buffer count per module:**
```
Static:  1 VBO + 1 IBO
Dynamic: 1 VBO + 1 IBO
Uniform: 1 UBO  (frame-wide, managed by renderer)
VAOs:    2 VAOs (one for static, one for dynamic — context-local in GL)
─────────────────────────────────────────────────
~7 objects per module  (vs. N per object)
```

---

## Per-Draw Uniform Data

Uniform data is opaque `ReadOnlySpan<byte>` passed per draw call. The shader owns the layout. The renderer allocates a frame-wide UBO; each draw's data is packed into it with alignment. Before each draw, a `glBindBufferRange` sets the correct UBO slice.

**Initial strategy:** UBO with `glBindBufferRange` (GL 3.1+).
**Upgrade path:** SSBO + `gl_DrawID` + `glMultiDrawElementsIndirect` (GL 4.3+) — entire module flushes in one draw call.

---

## Textures

Textures are provided per draw call (`ReadOnlySpan<IGpuTexture>`), not locked to a module. The module (shader) defines which sampler slots exist; the draw call fills them. This allows e.g. a sprite module where each sprite uses a different texture.

---

## Draw Call Internals (OpenGL backend)

Draw call data is collected as **blittable `GlDrawRecord` structs** — raw GL IDs and offsets, no managed references. The backend reads these at flush time and issues the actual GPU commands. This keeps the hot path as thin as possible and makes a native-C++ backend swap feasible in the future.

```
GlDrawRecord:
  BaseVertex      - index of first vertex in VBO  (for glDrawElementsBaseVertex)
  IndexByteOffset - byte offset into IBO
  IndexCount      - 0 = non-indexed draw
  VertexOffset    - byte offset into VBO (for non-indexed dynamic draws)
  VertexCount     - for non-indexed draws
  UniformOffset   - offset into the frame UBO
  UniformSize     - size of this draw's uniform data
  IsDynamic       - selects static vs dynamic VAO
```

---

## Frame Lifecycle

```
BeginFrame()
  - Reset all dynamic pool write cursors
  - Reset all pending draw lists
  - Reset frame UBO write cursor

// User code submits draws:
module.DrawStatic(handle, uniformData, textures)
module.DrawDynamic(vertices, indices, uniformData, textures)

EndFrame()
  - For each active viewport:
      MakeCurrent(viewport)
      Clear
      For each module (by RenderOrder, ascending):
        UseProgram + ApplyPipelineState  (once)
        BindStaticVAO or DynamicVAO as needed
        For each pending draw:
          BindBufferRange (UBO slice)
          BindTextures
          glDrawElementsBaseVertex / glDrawArrays
      Present(viewport)
```

---

## Pipeline State Application (once per module per frame)

- `gl.UseProgram(programId)`
- VAO bind (static or dynamic, switched per draw only when needed)
- Depth: `glDepthFunc`, `glDepthMask`, `glEnable/Disable(GL_DEPTH_TEST)`
- Blend: `glBlendFuncSeparate`, `glEnable/Disable(GL_BLEND)`
- Rasterizer: `glCullFace`, `glFrontFace`, `glPolygonMode`
- Primitive topology: passed to each `glDraw*` call

---

## Threading Model

**Initial implementation:** single GL context, all rendering on one thread. User must call `BeginFrame`/`EndFrame` from the same thread consistently. The GL context is initialized lazily on the first `CreateViewport` call from the user's thread.

**Upgrade path:** shared GL contexts (one per viewport), allowing multi-thread rendering. VAOs are context-local and must be created per context; buffers and textures are shared.

---

## Project Structure

```
src/
├── Name.Engine.Rendering/              interfaces + descriptors only
│   ├── IRenderer.cs
│   ├── IViewport.cs
│   ├── IRenderableModule.cs
│   ├── IDrawHandle.cs
│   ├── IShader.cs
│   ├── IGpuTexture.cs
│   └── Descriptors/
│       ├── RenderableModuleDescriptor.cs
│       ├── VertexLayout.cs
│       ├── VertexAttribute.cs
│       ├── VertexAttributeBaseType.cs
│       ├── PipelineState.cs
│       ├── BlendState.cs  /  BlendFactor.cs
│       ├── DepthState.cs  /  DepthFunc.cs
│       ├── RasterizerState.cs  /  CullMode.cs  /  FrontFace.cs  /  FillMode.cs
│       ├── PrimitiveTopology.cs
│       └── TextureFormat.cs
│
├── Name.Engine.Rendering.OpenGL/       GL backend
│   ├── OpenGLRenderingService.cs       IAppService entry point
│   ├── OpenGLRendererServer.cs         AppServer + IRenderer
│   ├── OpenGLViewport.cs
│   ├── OpenGLShader.cs
│   ├── OpenGLTexture.cs
│   ├── OpenGLRenderableModule.cs
│   ├── OpenGLDrawHandle.cs
│   ├── Pools/
│   │   ├── StaticGeometryPool.cs
│   │   └── DynamicGeometryPool.cs
│   └── Internal/
│       ├── GlDrawRecord.cs
│       ├── FrameUniformBuffer.cs
│       └── GlxNativeContext.cs         INativeContext for Silk.NET on Linux
│
└── Name.Engine.Platform/
    ├── Linux/
    │   ├── LibNames.cs                 add GL entry
    │   └── GLXApi.cs                   new — context creation on X11
    └── Windows/
        └── WGLApi.cs                   stub — context creation on Win32
```

---

## Open Decisions

1. **Module ordering** — `RenderOrder` int on descriptor. Lower = drawn first (e.g. 0 = opaque, 100 = transparent, 200 = UI).
2. **Uniform schema** — opaque `byte` span. Shader owns the layout. No validation in the rendering layer.
3. **Texture format support** — initial set: RGBA8, RGB8, R8, RGBA16F, Depth24Stencil8.
4. **Static pool growth** — initial implementation throws on overflow. Future: resize + re-upload.
5. **Multi-context** — deferred. Single context for now; upgrade path is explicit shared-context creation per viewport.
