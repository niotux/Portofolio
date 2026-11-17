# Renderer Design — Interactive Log

> Concrete OpenGL implementation first, abstractions later.
> Using Silk.NET v2.22.0.

---

## Ground Rules
- **Simple**: minimal API surface, no over-engineering
- **Smart**: design decisions informed by how OpenGL actually works
- **Zero-cost**: no unnecessary allocations, no redundant state changes, no wasted GPU work
- **No premature abstraction**: build the OpenGL renderer directly, extract interfaces once the shape is proven

---

## Design Session

### Q1: Mindset — No domain concepts

No "meshes", no "sprites", no "materials". We think in raw GPU terms:

1. **Pack data** — vertices, indices, uniforms — just bytes
2. **Upload to GPU** — buffers
3. **Draw** — tell the GPU how to interpret those bytes

The renderer doesn't know *what* it's drawing. It knows how to move bytes to the GPU and issue draw calls.

**Decision**: Pure data pipeline. Domain concepts live above the renderer, not inside it.

---

### Q2: Entry point — Renderer → Viewport

- You create a **Renderer** instance (owns the GL context / Silk.NET GL object)
- You pair it with a **Window** to get a **Viewport**
- The **Viewport** is your portal: it's what you talk to for drawing

**Decision**: Renderer + Window → Viewport. Viewport issues draw calls.

---

### Q3: Renderer vs Viewport responsibilities

**Renderer** = portal to the API, resource factory:
- Creates **shaders** (compiled programs — reusable across viewports)
- Creates **buffers** (vertex/index data on GPU — reusable)
- Creates **textures** (GPU images — reusable)
- Owns the Silk.NET `GL` object
- Produces Viewports

**Viewport** = the context, the draw target:
- Bound to a Window (one viewport per window)
- Owns the default framebuffer for that window
- Issues **draw calls** (bind shader + buffers + textures + state → draw)
- Handles **present** (swap buffers)
- Future: multi-viewport = multiple windows, each with its own viewport

Resources live on the Renderer (shared). Drawing happens on the Viewport (per-window).

**Decision**: Renderer owns GPU resources. Viewport owns draw submission and presentation.

---

### Q4: How does the user express a draw call?

The Viewport draws. A draw call needs:
- A **shader program**
- **Vertex data** in a buffer + vertex layout
- Optionally **index data**
- **Uniform data** (transforms, colors, etc.)
- **Textures** bound to sampler slots
- **Pipeline state** (blend, depth, cull, etc.)

How does the user bundle this? Options:

A. **Stateful** — set shader, set buffer, set state on the viewport, then call `Draw()` (like raw GL)
B. **Command struct** — submit a self-contained draw command with everything in it
C. **Hybrid** — some state is "bound" (shader, pipeline), some is per-draw (buffers, uniforms)

**Decision**: *(deferred — need to define data structure first)*

---

### Q4a: Renderable Module — state grouping

**Problem**: Switching shader + vertex layout + pipeline state per object is expensive. If you have 500 world objects, 20 water surfaces, and 1 skybox — all with different shaders — you don't want 521 state switches. You want 3.

**Solution**: Group by state into **Renderable Modules**. A module defines:

- **Shader program** — how to interpret and shade the data
- **Vertex layout** — what the vertex bytes mean (position, UV, normal, etc.)
- **Pipeline state** — blend, depth, cull, etc.

These are the "expensive to switch" things. You bind them **once per module**, then draw everything inside that module.

Inside a module, you have:

- **Geometry data** — vertex + index bytes, packed linearly
  - **Static**: uploaded once, persistent (terrain, buildings)
  - **Dynamic**: changes every frame, transient (particles, animated stuff)
- **Uniform / UBO data** — per-draw parameters (transforms, colors, material props)
- **Texture bindings** — per-draw (different objects can use different textures within the same module)

So the frame looks like:

```
for each module (sorted by render order):
    bind module state (shader + layout + pipeline)  ← once
    for each draw in module:
        bind per-draw uniforms + textures            ← cheap
        draw(vertex range)                           ← the actual work
```

**Key questions still open:**

1. **Static vs dynamic geometry** — how do these coexist inside a module?
   - Static: bump-allocated, persistent across frames, rarely changes
   - Dynamic: ring-buffered or reset each frame, written every frame
   - Do they share a buffer? Separate buffers? Separate VAOs?

2. **Uniforms** — how does per-draw uniform data get to the GPU?
   - Option: single UBO per module, pack all per-draw data contiguously, `glBindBufferRange` per draw
   - Option: push constants style (uniform calls per draw — simpler, but more driver overhead)

3. **Texture bindings** — per-module or per-draw?
   - Per-draw is more flexible (different objects, different textures, same shader)
   - But means more bind calls inside the inner loop

4. **Module ordering** — who decides the draw order between modules?
   - User-specified render order? (opaque=0, transparent=100, UI=200)
   - Or automatic based on pipeline state? (opaque first, then blended)

**Decision**: Renderable Module = state bucket (shader + layout + pipeline). Geometry + uniforms + textures packed inside. Details pending.

---

### Q5: Global Buffer Architecture — "Hotel, not houses"

Instead of each module owning its own GPU buffers, all data lives in shared global buffers. Modules hold **keys** — offsets and sizes into the shared memory.

**The buffers (owned by Renderer):**

| Buffer | Contents | Lifetime | Write strategy |
|--------|----------|----------|----------------|
| Global Static VBO | All persistent vertex data, all modules | Lives until freed | Bump allocator, upload once |
| Global Static EBO | All persistent index data, all modules | Lives until freed | Same |
| Global Dynamic VBO | All per-frame vertex data, all modules | Reset each frame | Write linearly, reset head to 0 |
| Global Dynamic EBO | All per-frame index data, all modules | Reset each frame | Same |
| Global UBO | All per-draw uniform data, all modules | Reset each frame | Pack contiguously per frame |

**5 buffers total**, regardless of module count.

**What a module holds** (no GPU resources of its own):
- Shader program handle (GL ID)
- VAO handle (defines vertex layout, points at the global buffers)
- Pipeline state (blend, depth, cull — just CPU-side values)
- Static region keys: `{vertexOffset, vertexSize, indexOffset, indexSize}` into static buffers
- Dynamic: writes fresh each frame into the dynamic buffers, gets back transient offsets
- Draw list: records of what to draw this frame (offsets into buffers + UBO slices)

**Why this works with OpenGL:**
- Different VAOs can point at the **same buffer** with different attribute layouts/offsets
- `glDrawElements` / `glDrawElementsBaseVertex` take offset parameters — draws target regions within the global buffer
- `glBindBufferRange` on the UBO selects a per-draw slice — no rebinding the whole buffer
- Only VAO + shader + pipeline state switch per module (the unavoidable minimum)

**Why static and dynamic are separate buffers:**
- Mapping a buffer to write dynamic data while the GPU reads static data from the same buffer = pipeline stall
- Separate buffers = GPU reads static freely while CPU writes dynamic freely
- Dynamic buffers can use `GL_STREAM_DRAW` / orphaning / persistent mapping
- Static buffers use `GL_STATIC_DRAW`, never touched after upload

**Frame lifecycle:**

```
BeginFrame:
    reset dynamic VBO/EBO write heads to 0
    reset UBO write head to 0

User code (per module):
    write dynamic geometry → appended to global dynamic VBO/EBO
    write per-draw uniforms → appended to global UBO
    record draw commands (offsets + sizes)

Flush (per viewport):
    upload dynamic VBO/EBO + UBO to GPU
    for each module (sorted by render order):
        bind VAO + shader + pipeline state          ← per-module (expensive, done once)
        for each draw in module:
            glBindBufferRange(UBO, draw's slice)     ← per-draw (cheap)
            bind textures                            ← per-draw (cheap)
            glDrawElements(offset into global buf)   ← the work

Present:
    swap buffers
```

**Decision**: Global shared buffers. Modules hold keys (offsets), not resources. 5 buffers total: static VBO/EBO, dynamic VBO/EBO, UBO.

---

### Q6: Module allocation — how do modules claim space?

Modules need to "check in" to the hotel. How does a module get its space?

**Static geometry:**
- Module calls something like `AllocateStatic(vertexData, indexData)` on the Renderer
- Renderer bump-allocates into the global static VBO/EBO
- Returns a handle (offset + size) that the module stores
- To free: return the region (free-list, or just leak it for now — compaction later)

**Dynamic geometry:**
- Each frame, module calls something like `WriteDynamic(vertexData, indexData)`
- Renderer appends to the dynamic buffer's write head
- Returns transient offset (only valid this frame)
- Next frame, write head resets — old data is gone

**Per-draw uniforms:**
- Module packs uniform data as raw bytes (`ReadOnlySpan<byte>`)
- Renderer appends to the UBO, respects `GL_UNIFORM_BUFFER_OFFSET_ALIGNMENT`
- Returns the aligned offset for `glBindBufferRange`

**Decision**: Module owns the allocation API for geometry. Renderer manages the global buffer internals.

---

### Q8: Draw command — the module IS the command

The module is the complete recipe. The viewport just says "draw this module."

**The module accumulates a draw list** — each entry is one "object":

```csharp
struct DrawEntry
{
    int IndexOffset;       // where in the global EBO
    int IndexCount;        // how many indices
    int BaseVertex;        // offset into global VBO (for glDrawElementsBaseVertex)
    bool IsStatic;         // static or dynamic buffer?
    int UboOffset;         // aligned offset into global UBO
    int UboSize;           // size of uniform data for this draw
    uint Texture0;         // GL texture ID for slot 0
    uint Texture1;         // GL texture ID for slot 1
    // ... (fixed number of slots, expandable later)
}
```

**The user adds entries via the module:**
```
module.DrawStatic(staticHandle, uniformData, texture0, texture1)
module.DrawDynamic(dynamicOffset, count, uniformData, texture0, texture1)
```

**The viewport flushes:**
```
viewport.Draw(module)   // binds module state, iterates draw list, issues GL calls
```

**Why this is the most general approach (Level 0):**
- One `glDrawElements` per draw entry — simple, correct, predictable
- Per-entry texture bindings — full flexibility, no assumptions
- Users who want batching (texture arrays, atlases) can do it themselves:
  just make one draw entry for the whole module and pass a layer index through uniforms
- The renderer doesn't prevent optimization — it just doesn't force it
- Texture binds in the inner loop are cheap (the expensive shader/VAO/pipeline swap is per-module)

**Draw list is transient** — reset each frame during BeginFrame. Static geometry persists in the buffer, but the *decision to draw it* is per-frame.

**Decision**: Module owns draw list. Per-entry texture bindings (Level 0). Most general, doesn't prevent higher-level batching.

---

### Open questions remaining:

1. ~~**Buffer growth**~~ — **Fixed-size buffers. Pre-sized at renderer creation. Throw on overflow. No runtime allocation after init.** Future upgrade: paged allocation.
2. ~~**Module ordering**~~ — **Insertion order. Modules draw in the order they were added. No sorting, no priority ints.** User controls order by controlling insertion order.
3. ~~**Textures**~~ — **Full control. User specifies format, filtering (min/mag/mipmap), wrapping (S/T/R), and target. Nothing hardcoded.** See Q9.
4. ~~**Pipeline state**~~ — **Part of the renderable module. User sets it on the module. Applied once per module before its draws.** See Q10.
5. ~~**Concrete class design**~~ — **Everything starts as a class. Optimize to structs later once the shape is proven and we know what's in the hot path.** See Q11.

---

### Q7: Shaders — SPIR-V from GLSL

**Pipeline:**
```
GLSL (.vert/.frag) → glslc (offline) → SPIR-V (.spv) → load bytes at runtime
```

**At runtime (Renderer does this):**
```c
glCreateShader(GL_VERTEX_SHADER)
glShaderBinary(1, &shader, GL_SHADER_BINARY_FORMAT_SPIR_V, spvBytes, len)
glSpecializeShader(shader, "main", 0, null, null)
// repeat for fragment
glCreateProgram()
glAttachShader(program, vert)
glAttachShader(program, frag)
glLinkProgram(program)
```

**Ownership:**
- Renderer creates and owns shader programs (shared GPU resource)
- Module holds a program handle (GL ID) — doesn't own it, just references it
- Multiple modules can share the same shader program

**API shape (on Renderer):**
```
CreateShaderProgram(ReadOnlySpan<byte> vertSpv, ReadOnlySpan<byte> fragSpv) → uint (GL program ID)
```

**Requires:** OpenGL 4.6 or `GL_ARB_gl_spirv`

**Decision**: SPIR-V shaders, compiled offline with `glslc`, loaded as binary at runtime. Renderer owns programs, modules reference them.

---

### Q9: Textures — full user control

**Created by Renderer, all parameters user-specified:**

```csharp
renderer.CreateTexture(
    target,          // Texture2D, Texture2DArray, CubeMap, Texture3D, ...
    width, height,   // dimensions (+ depth for 3D/arrays)
    internalFormat,  // RGBA8, RGB8, R8, RGBA16F, Depth24Stencil8, ...
    pixelFormat,     // RGBA, RGB, Red, DepthStencil, ...
    pixelType,       // UnsignedByte, Float, UnsignedInt248, ...
    pixelData,       // ReadOnlySpan<byte> (or null for empty, e.g. render targets)
    minFilter,       // Nearest, Linear, NearestMipmapLinear, LinearMipmapLinear, ...
    magFilter,       // Nearest, Linear
    wrapS,           // Repeat, ClampToEdge, MirroredRepeat
    wrapT,           // same
    wrapR,           // same (for 3D/cubemap)
    generateMipmaps  // bool — call glGenerateMipmap after upload?
) → uint (GL texture ID)
```

**What this maps to in GL:**
```c
glGenTextures(1, &id)
glBindTexture(target, id)
glTexImage2D(target, 0, internalFormat, w, h, 0, pixelFormat, pixelType, data)
glTexParameteri(target, GL_TEXTURE_MIN_FILTER, minFilter)
glTexParameteri(target, GL_TEXTURE_MAG_FILTER, magFilter)
glTexParameteri(target, GL_TEXTURE_WRAP_S, wrapS)
glTexParameteri(target, GL_TEXTURE_WRAP_T, wrapT)
glTexParameteri(target, GL_TEXTURE_WRAP_R, wrapR)    // for 3D/cube
if (generateMipmaps) glGenerateMipmap(target)
```

**Ownership:** Renderer creates and owns textures. Modules/draw entries reference them by GL ID.

**No image loading** — the renderer doesn't know about PNG, JPG, etc. It takes raw pixel bytes. Image decoding is the user's responsibility (StbImageSharp, ImageSharp, whatever).

**Decision**: Full user control over all texture parameters. Renderer takes raw bytes + GL parameters, returns a handle. No hardcoded defaults.

---

### Q10: Pipeline state — module-level settings

Pipeline state lives on the module. Set once, applied before all draws in that module.

**Fields:**

```csharp
// Blend
bool BlendEnabled;
BlendFactor SrcColor;       // One, SrcAlpha, OneMinusSrcAlpha, ...
BlendFactor DstColor;       // same
BlendFactor SrcAlpha;       // same
BlendFactor DstAlpha;       // same

// Depth
bool DepthTestEnabled;
bool DepthWriteEnabled;
DepthFunc DepthFunction;     // Less, LEqual, Greater, Always, ...

// Rasterizer
CullMode CullMode;           // None, Front, Back
FrontFace FrontFace;         // CounterClockwise, Clockwise
FillMode FillMode;           // Solid, Wireframe

// Primitive
PrimitiveTopology Topology;  // Triangles, Lines, Points
```

**What this maps to in GL (applied once per module):**
```c
// Blend
if (blend) { glEnable(GL_BLEND); glBlendFuncSeparate(...); }
else glDisable(GL_BLEND);

// Depth
if (depth) { glEnable(GL_DEPTH_TEST); glDepthFunc(...); }
else glDisable(GL_DEPTH_TEST);
glDepthMask(depthWrite);

// Rasterizer
if (cull != None) { glEnable(GL_CULL_FACE); glCullFace(...); }
else glDisable(GL_CULL_FACE);
glFrontFace(...);
glPolygonMode(GL_FRONT_AND_BACK, fill);
```

**Decision**: Pipeline state is a set of fields on the module. Full user control, no presets. Applied once before the module's draw list is flushed.

