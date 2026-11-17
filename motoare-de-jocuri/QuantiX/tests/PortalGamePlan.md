I need to plan a non-euclidean Portal-like game as a new test project for the QuantiX engine. The project lives under tests/QuantiX.Engine.Tests.Portal/.

## Engine Context

The engine uses:
- `AppServer` base class with `OnRun()` for the game loop
- `App` orchestrator with service registration (WindowingService, AudioService)
- `QuartzRenderer` (OpenGL via Silk.NET) implementing `IRenderer`
- `IViewport` for window rendering (Draw + Present)
- `IFramebuffer` for offscreen rendering (Draw, has DepthTexture and optional ColorTexture)
- `Pipeline` = Shader + PipelineState + VertexAttributes + DrawEntry[]
- Console-based input (Console.KeyAvailable / Console.ReadKey)
- Vec3&lt;float&gt; math library with Cross, Dot (no Normalize/Length - must compute inline)
- Mat4 utilities: Multiply, Perspective, LookAt, Ortho (in test projects, not engine)
- AABB collision (Physics.MoveAndSlide) in FPS test project
- GLSL shaders with #version 420 core, layout(binding=N) for samplers
- PipelineState supports: CullMode, FillMode, FrontFace, DepthFunc, DepthWrite, BlendFactor, Topology, DepthBiasConstant/Slope

## Game Concept

A first-person non-euclidean puzzle/exploration game where:
- The world consists of rooms (simple box geometry with colored/textured walls, floor, ceiling)
- Rooms are connected through portals - rectangular openings on walls
- Portals are non-euclidean: rooms connect in spatially impossible ways (e.g., 4 rooms each linked by right turns forming a loop, a corridor that's longer inside than outside, rooms that overlap in 3D space)
- Walking through a portal seamlessly teleports you to the connected room
- Looking through a portal shows the destination room rendered correctly from the virtual camera position
- Basic physics: gravity, floor/wall collision, walking

## Technical Approach for Portal Rendering

The engine already has IFramebuffer with color texture support. For each visible portal:
1. Compute virtual camera position by transforming player camera through the portal's transform
2. Render the destination room to a framebuffer (color + depth)
3. Render the portal surface as a textured quad using the framebuffer's color texture

This avoids needing stencil buffer support.

## Key Design Questions to Address

1. How should the room/portal data structure work?
2. How to compute the virtual camera transform through a portal?
3. How to handle portal rendering depth (portal within a portal)?
4. How to detect and handle player teleportation through portals?
5. What non-euclidean room layouts to create?
6. How to handle oblique near-plane clipping (so you don't see behind the portal)?

## Files to Create

```
tests/QuantiX.Engine.Tests.Portal/
├── QuantiX.Engine.Tests.Portal.csproj
├── Program.cs
├── PortalServer.cs
├── Room.cs
├── Portal.cs / PortalLink.cs
├── World.cs
├── Camera.cs
├── Physics.cs
├── Mat4.cs
└── MusicSource.cs (optional)
```

## Existing Patterns to Reuse

- Program.cs pattern from FPS test (App + service registration)
- Camera from FPS (acceleration-based movement, smooth look)
- Mat4 utilities from rendering test (Perspective, LookAt, Multiply, Ortho)
- Physics.MoveAndSlide concept from FPS (AABB collision)
- Pipeline/shader creation patterns from both tests
- MusicSource from rendering test

Please design a detailed implementation plan covering:
1. Data structures (Room, Portal, World)
2. Portal rendering algorithm (framebuffer-based)
3. Virtual camera computation
4. Teleportation detection and handling
5. Non-euclidean world layout (specific room configurations)
6. Room geometry generation (mesh building)
7. Shader design (world shader with per-room coloring)
8. Physics and collision within rooms
9. Oblique near-plane clipping for portal rendering
10. Implementation order and dependencies