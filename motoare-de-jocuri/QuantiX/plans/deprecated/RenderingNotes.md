# Rendering System — Raw Notes

Ideas and corrections to be folded into the main plan once enough are collected.

---

## Note 1 — Multi-Viewport & Central Component

- The system is fundamentally multi-viewport.
- `Surface` = the OS-level window (already exists).
- There is a **central component** (currently called `IRenderer`) that bonds with the graphics API (creates the GL context, manages shared resources, etc.). It is NOT itself the thing you draw to — it's the hub.
- From that central component you get a **Viewport** (or Canvas — naming TBD). The Viewport is the drawable target bound to a Surface.
- Viewports are independent of each other and can be drawn to from multiple threads.
- The central component is shared; the Viewports are the per-surface, per-thread drawing units.

---

## Note 2 — Modules: The GPU Portal

Modules are the stateful "scene" layer — the bridge between C# and the GPU. No scene graph, no GameObjects, just GPU state organized in a convenient managed wrapper.

**Key principle:** everything is GPU-owned. C# holds handles (GPU resource IDs / pointers). No dimensionality is hardcoded — no preset 2D/3D attributes. All attribute layout is defined per-module, per-shader.

### What a module holds

A module is a collection of GPU handles that together describe a complete renderable state:

- **Shader program** — compiled GPU program; vertex attributes are defined by and belong to this shader
- **Vertex attributes** — described per-shader (location, type, count, stride, offset). No global convention.
- **Geometry buffers** — VBO(s) and optionally an IBO; can be static or dynamic (see pool note)
- **Uniform data** — UBO(s) or equivalent; per-frame or per-draw constant data
- **Textures** — GPU texture handles + sampler state, referenced by the shader

### What OpenGL needs from a module

| Resource | GL object | Notes |
|---|---|---|
| Shader program | `GLuint` program ID | Compiled from GLSL source |
| Vertex layout | `GLuint` VAO ID | Describes attribute layout for this shader |
| Vertex data | `GLuint` VBO ID | Geometry, format defined by shader attributes |
| Index data | `GLuint` IBO ID | Optional; enables indexed drawing |
| Uniforms | `GLuint` UBO ID | Per-draw or per-frame data block |
| Textures | `GLuint[]` texture IDs | Bound to sampler slots defined in shader |

### C# design

- GPU resources are wrapped in thin managed C# classes (`GpuBuffer`, `GpuTexture`, `GpuShader`, etc.), each holding a native handle (`uint` for GL, `IntPtr`/pointer for Vulkan/native).
- The module collects these handles into a coherent structure.
- The C# layer manages lifetimes (create/destroy), but does not interpret the data.

### Draw call design

Draw calls are **simple blittable structs** — they reference handles and offsets, nothing more. The backend (could be native C++) reads these structs and issues the actual API commands. This keeps the hot submission path as thin as possible and backend-swappable.

A draw call struct contains (approx):
- Reference to the module (pipeline state is already known)
- Base vertex + vertex count (or base index + index count for indexed draws)
- Uniform data offset (into the frame's UBO)
- Texture slot bindings (indices into the module's texture list)
- Instance count (1 for non-instanced)

---

## Note 3 — GLX/WGL placement & cross-platform

- The GLX P/Invoke bindings currently live in `Name.Engine.Platform`. This was inspired by the existing audio/windowing pattern, but **GLX and WGL are rendering concerns** — they belong in `Name.Engine.Rendering.OpenGL`. Future work: move them there.
- The OpenGL backend must not be X11/Linux hardcoded. All platform-specific context creation (GLX on Linux, WGL on Windows) must be behind an **`IGlContextManager`** internal interface — same pattern as `ISurfaceManager`.
- `OpenGLRendererServer` and `OpenGLViewport` must have zero `OperatingSystem.Is*()` checks in their main flow. Platform selection happens once at service construction, in `OpenGLRenderingService`.
- `IGlContextManager` responsibility:
  - `Initialize()` — set up the platform context system (open display, choose FBConfig, etc.)
  - `GetProcAddress(name)` — load GL function pointers (used by Silk.NET)
  - `CreateContext(shareContext)` — create a GL context
  - `DestroyContext(ctx)` — cleanup
  - `CreateDrawable(SurfaceHandle)` — create a platform-side drawable from a surface
  - `DestroyDrawable(drawable)` — cleanup
  - `MakeCurrent(ctx, drawable)` — bind for rendering
  - `MakeCurrentSurfaceless(ctx)` — bind without drawable (for resource loading)
  - `SwapBuffers(drawable)` — present
