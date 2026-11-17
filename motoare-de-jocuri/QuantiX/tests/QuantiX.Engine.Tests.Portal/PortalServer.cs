using System.Text;
using QuantiX.Engine.Mathematics.Vectors;
using QuantiX.Engine.Rendering;
using QuantiX.Engine.Windowing;
using QuantiX.Engine.Renderers.Quartz;

namespace QuantiX.Engine.Tests.Portal;

public class PortalServer : AppServer
{
  public WindowingService Windowing { get; } = new();

  // Per-room GPU resources
  private sealed class RoomGpu(IBuffer vb, IBuffer ib, uint indexCount) : IDisposable
  {
    public readonly IBuffer Verts = vb;
    public readonly IBuffer Inds = ib;
    public readonly uint IndexCount = indexCount;
    public void Dispose() { Verts.Dispose(); Inds.Dispose(); }
  }

  // Per-portal GPU resources (the portal quad that shows the destination)
  private sealed class PortalQuadGpu(IBuffer vb, IBuffer ib) : IDisposable
  {
    public readonly IBuffer Verts = vb;
    public readonly IBuffer Inds = ib;
    public void Dispose() { Verts.Dispose(); Inds.Dispose(); }
  }

  protected override void OnRun()
  {
    Window window = new("QuantiX: Non-Euclidean Portal Demo", 1024, 768);
    Windowing.Server.Start();
    Windowing.Manager.Open(window);
    window.OnDestroy += this.Stop;

    using IRenderer renderer = new QuartzRenderer();
    using IViewport viewport = renderer.CreateViewport(window);

    // ── Build world ──
    Room[] rooms = WorldBuilder.BuildWorld();
    int currentRoom = 0;
    Camera cam = new() { Position = new(0, Camera.EyeHeight, 0), Yaw = 0, Pitch = 0 };

    // ── Upload room meshes ──
    var roomGpus = new RoomGpu[rooms.Length];
    for (int i = 0; i < rooms.Length; i++)
    {
      var (verts, inds) = rooms[i].BuildMesh();
      var vb = renderer.CreateBuffer(verts.Length * sizeof(float));
      var ib = renderer.CreateBuffer(inds.Length * sizeof(uint));
      vb.Write<float>(verts, 0);
      ib.Write<uint>(inds, 0);
      roomGpus[i] = new RoomGpu(vb, ib, (uint)inds.Length);
    }

    // ── Upload portal quads ──
    // portalQuads[roomIndex][portalIndex]
    var portalQuads = new PortalQuadGpu[rooms.Length][];
    for (int i = 0; i < rooms.Length; i++)
    {
      portalQuads[i] = new PortalQuadGpu[rooms[i].Portals.Count];
      for (int j = 0; j < rooms[i].Portals.Count; j++)
      {
        var (qv, qi) = rooms[i].BuildPortalQuad(rooms[i].Portals[j]);
        var vb = renderer.CreateBuffer(qv.Length * sizeof(float));
        var ib = renderer.CreateBuffer(qi.Length * sizeof(uint));
        vb.Write<float>(qv, 0);
        ib.Write<uint>(qi, 0);
        portalQuads[i][j] = new PortalQuadGpu(vb, ib);
      }
    }

    // ── Build collision data per room ──
    var roomColliders = new AABB[rooms.Length][];
    for (int i = 0; i < rooms.Length; i++)
      roomColliders[i] = rooms[i].BuildColliders();

    // ── Portal framebuffers (match viewport size for screen-space parallax) ──
    int maxPortals = rooms.Max(r => r.Portals.Count);
    var (initW, initH) = viewport.Size;
    var portalFBs = new IFramebuffer[maxPortals];
    for (int i = 0; i < maxPortals; i++)
      portalFBs[i] = renderer.CreateFramebuffer(initW, initH, TextureFormat.Depth32F, TextureFormat.RGBA8);

    // ── Shaders ──
    using IShader roomShader = CreateRoomShader(renderer);
    using IShader portalQuadShader = CreatePortalQuadShader(renderer);
    using IShader crosshairShader = CreateCrosshairShader(renderer);

    // ── Uniform buffers ──
    // Room UBO: mat4 MVP (64) + vec4 lightDir (16) + vec4 ambient (16) = 96 bytes
    using IBuffer roomUB = renderer.CreateBuffer(96);
    // Portal quad UBO: mat4 MVP (64) + vec4 screenSize (16) = 80 bytes
    using IBuffer portalQuadUB = renderer.CreateBuffer(80);

    // ── Dummy texture (room shader needs binding 0 even though we use vertex colors) ──
    byte[] white = [255, 255, 255, 255];
    using ITexture dummyTex = renderer.CreateTexture2D(white, 1, 1, TextureFormat.RGBA8,
      TextureMagFilter.Nearest, TextureMinFilter.Nearest, TextureWrap.ClampToEdge);

    // ── Room pipeline state ──
    PipelineState roomState = new()
    {
      CullMode = CullMode.Back,
      FillMode = FillMode.Solid,
      FrontFace = FrontFace.CounterClockwise,
      DepthFunc = DepthFunc.Less,
      DepthWrite = true,
      SrcBlendFactor = BlendFactor.One,
      DstBlendFactor = BlendFactor.Zero,
      Topology = PrimitiveTopology.Triangles,
    };

    // Room vertex attributes: pos(3) + normal(3) + color(3) = 9 floats
    uint roomStride = 9 * sizeof(float);
    VertexAttribute[] roomAttribs =
    [
      new() { Location = 0, Size = 3, Stride = roomStride, Offset = 0 },
      new() { Location = 1, Size = 3, Stride = roomStride, Offset = 3 * sizeof(float) },
      new() { Location = 2, Size = 3, Stride = roomStride, Offset = 6 * sizeof(float) },
    ];

    // ── Portal quad pipeline state ──
    PipelineState portalState = new()
    {
      CullMode = CullMode.None,
      FillMode = FillMode.Solid,
      FrontFace = FrontFace.CounterClockwise,
      DepthFunc = DepthFunc.Less,
      DepthWrite = true,
      SrcBlendFactor = BlendFactor.One,
      DstBlendFactor = BlendFactor.Zero,
      Topology = PrimitiveTopology.Triangles,
    };

    // Portal quad vertex attributes: pos(3) + texcoord(2) = 5 floats
    uint portalStride = 5 * sizeof(float);
    VertexAttribute[] portalAttribs =
    [
      new() { Location = 0, Size = 3, Stride = portalStride, Offset = 0 },
      new() { Location = 1, Size = 2, Stride = portalStride, Offset = 3 * sizeof(float) },
    ];

    // ── Crosshair pipeline ──
    var (crosshairPipeline, crosshairRes) = CreateCrosshairPipeline(renderer, crosshairShader);

    // ── Light direction (fixed overhead-forward) ──
    float lx = 0.4f, ly = 0.8f, lz = -0.3f;
    float ll = MathF.Sqrt(lx * lx + ly * ly + lz * lz);
    lx /= ll; ly /= ll; lz /= ll;

    PrintControls();

    int frameCount = 0;
    long lastFpsTick = Environment.TickCount64;

    while (this.State == AppServerState.Running)
    {
      frameCount++;
      long now = Environment.TickCount64;
      if (now - lastFpsTick >= 1000)
      {
        window.Title = $"QuantiX Portal [ {frameCount} FPS | Room: {rooms[currentRoom].Name} ]";
        frameCount = 0;
        lastFpsTick = now;
      }

      // ── Input ──
      while (Console.KeyAvailable)
      {
        var key = Console.ReadKey(true).Key;
        switch (key)
        {
          case ConsoleKey.W: cam.MoveForward(); break;
          case ConsoleKey.S: cam.MoveBackward(); break;
          case ConsoleKey.D: cam.MoveRight(); break;
          case ConsoleKey.A: cam.MoveLeft(); break;
          case ConsoleKey.J: case ConsoleKey.LeftArrow: cam.LookLeft(); break;
          case ConsoleKey.L: case ConsoleKey.RightArrow: cam.LookRight(); break;
          case ConsoleKey.I: case ConsoleKey.UpArrow: cam.LookUp(); break;
          case ConsoleKey.K: case ConsoleKey.DownArrow: cam.LookDown(); break;
          case ConsoleKey.H: PrintControls(); break;
          case ConsoleKey.Q: case ConsoleKey.Escape: this.Stop(); break;
        }
      }

      // ── Collision ──
      Vec3<float> oldPos = cam.Position;
      float r = Camera.PlayerRadius;
      AABB playerBox = new(
        new(cam.Position.X - r, cam.Position.Y - Camera.EyeHeight, cam.Position.Z - r),
        new(cam.Position.X + r, cam.Position.Y - Camera.EyeHeight + Camera.PlayerHeight, cam.Position.Z + r));

      // Simple: if new position collides, revert
      bool collides = false;
      foreach (ref readonly var col in roomColliders[currentRoom].AsSpan())
      {
        if (playerBox.Intersects(col)) { collides = true; break; }
      }
      if (collides)
        cam.Position = oldPos;

      // ── Portal teleportation ──
      CheckPortalTeleport(rooms, ref currentRoom, cam);

      // ── Compute VP matrix ──
      var (vpW, vpH) = viewport.Size;
      float aspect = vpH > 0 ? (float)vpW / vpH : 1f;
      float[] mainVP = cam.GetViewProjection(aspect);

      // ── Portal rendering pass: render each portal's destination view into framebuffers ──
      var portalList = rooms[currentRoom].Portals;
      for (int pi = 0; pi < portalList.Count; pi++)
      {
        var portal = portalList[pi];
        int destIdx = portal.DestRoomIndex;

        // Virtual camera: transform player camera through portal
        Vec3<float> virtualPos = Mat4.TransformPoint(portal.Transform, cam.Position);
        float virtualYaw = cam.Yaw + portal.YawDelta;

        Camera virtualCam = new() { Position = virtualPos, Yaw = virtualYaw, Pitch = cam.Pitch };
        float[] portalVP = virtualCam.GetViewProjection(aspect);

        // Upload room UBO for dest room
        roomUB.Write<float>(portalVP, 0);
        roomUB.Write<float>([lx, ly, lz, 0], 64);
        roomUB.Write<float>([0.25f, 0, 0, 0], 80);

        DrawEntry destEntry = new(
          textures: [dummyTex],
          vertexBuffer: roomGpus[destIdx].Verts,
          indexBuffer: roomGpus[destIdx].Inds,
          uniformBuffer: roomUB,
          vertexOffset: 0,
          indexStart: 0,
          indexCount: roomGpus[destIdx].IndexCount,
          uniformStart: 0,
          uniformSize: 96);

        Pipeline destPipeline = new(roomShader, roomState, roomAttribs, [destEntry]);
        portalFBs[pi].Draw([destPipeline]);
      }

      // ── Main pass: render current room + portal quads + crosshair ──

      // Room UBO for current room
      roomUB.Write<float>(mainVP, 0);
      roomUB.Write<float>([lx, ly, lz, 0], 64);
      roomUB.Write<float>([0.25f, 0, 0, 0], 80);

      DrawEntry roomEntry = new(
        textures: [dummyTex],
        vertexBuffer: roomGpus[currentRoom].Verts,
        indexBuffer: roomGpus[currentRoom].Inds,
        uniformBuffer: roomUB,
        vertexOffset: 0,
        indexStart: 0,
        indexCount: roomGpus[currentRoom].IndexCount,
        uniformStart: 0,
        uniformSize: 96);

      Pipeline roomPipeline = new(roomShader, roomState, roomAttribs, [roomEntry]);

      // Portal quad entries (textured with framebuffer color textures, screen-space UVs)
      portalQuadUB.Write<float>(mainVP, 0);
      portalQuadUB.Write<float>([(float)vpW, (float)vpH, 0, 0], 64);

      var portalDrawEntries = new DrawEntry[portalList.Count];
      for (int pi = 0; pi < portalList.Count; pi++)
      {
        portalDrawEntries[pi] = new DrawEntry(
          textures: [portalFBs[pi].ColorTexture!],
          vertexBuffer: portalQuads[currentRoom][pi].Verts,
          indexBuffer: portalQuads[currentRoom][pi].Inds,
          uniformBuffer: portalQuadUB,
          vertexOffset: 0,
          indexStart: 0,
          indexCount: 6,
          uniformStart: 0,
          uniformSize: 80);
      }

      Pipeline portalPipeline = new(portalQuadShader, portalState, portalAttribs,
        portalDrawEntries.Length > 0 ? portalDrawEntries : []);

      var pipelines = portalDrawEntries.Length > 0
        ? new[] { roomPipeline, portalPipeline, crosshairPipeline }
        : new[] { roomPipeline, crosshairPipeline };

      viewport.Draw(pipelines);
      viewport.Present();
      window.Update();
    }

    // ── Cleanup ──
    crosshairRes.Dispose();
    foreach (var fb in portalFBs) fb.Dispose();
    foreach (var rg in roomGpus) rg.Dispose();
    foreach (var pqs in portalQuads)
      foreach (var pq in pqs) pq.Dispose();
  }

  // ── Portal teleportation ──

  private static void CheckPortalTeleport(Room[] rooms, ref int currentRoom, Camera cam)
  {
    var room = rooms[currentRoom];
    float threshold = 0.15f;

    foreach (var portal in room.Portals)
    {
      // Check if player is close to and past the portal plane
      bool crossed = portal.Wall switch
      {
        WallSide.East => cam.Position.X > room.Width / 2f - threshold &&
                         IsWithinPortal(cam.Position.Z, cam.Position.Y, portal, room),
        WallSide.West => cam.Position.X < -room.Width / 2f + threshold &&
                         IsWithinPortal(-cam.Position.Z, cam.Position.Y, portal, room),
        WallSide.North => cam.Position.Z < -room.Depth / 2f + threshold &&
                          IsWithinPortal(cam.Position.X, cam.Position.Y, portal, room),
        WallSide.South => cam.Position.Z > room.Depth / 2f - threshold &&
                          IsWithinPortal(-cam.Position.X, cam.Position.Y, portal, room),
        _ => false
      };

      if (!crossed) continue;

      // Teleport!
      cam.Position = Mat4.TransformPoint(portal.Transform, cam.Position);
      cam.Yaw += portal.YawDelta;
      currentRoom = portal.DestRoomIndex;

      Console.WriteLine($"  >> Teleported to: {rooms[currentRoom].Name}");
      return;
    }
  }

  private static bool IsWithinPortal(float alongPos, float yPos, PortalOpening p, Room room)
  {
    float wallExt = room.WallExtent(p.Wall);
    float portalStart = -wallExt / 2f + p.OffsetAlongWall;
    float portalEnd = portalStart + p.Width;
    return alongPos >= portalStart && alongPos <= portalEnd &&
           yPos >= p.OffsetY && yPos <= p.OffsetY + p.Height + Camera.EyeHeight;
  }

  // ── Shaders ──

  private static IShader CreateRoomShader(IRenderer renderer)
  {
    string vertSrc =
    """
    #version 420 core
    layout(location = 0) in vec3 aPos;
    layout(location = 1) in vec3 aNormal;
    layout(location = 2) in vec3 aColor;

    layout(std140) uniform RoomData {
      mat4 uMVP;
      vec4 uLightDir;
      vec4 uAmbient;
    };

    out vec3 vNormal;
    out vec3 vColor;
    out vec3 vWorldPos;

    void main()
    {
      gl_Position = uMVP * vec4(aPos, 1.0);
      vNormal = aNormal;
      vColor = aColor;
      vWorldPos = aPos;
    }
    """;

    string fragSrc =
    """
    #version 420 core
    in vec3 vNormal;
    in vec3 vColor;
    in vec3 vWorldPos;
    out vec4 FragColor;

    layout(std140) uniform RoomData {
      mat4 uMVP;
      vec4 uLightDir;
      vec4 uAmbient;
    };

    void main()
    {
      vec3 N = normalize(vNormal);
      vec3 L = normalize(uLightDir.xyz);

      float NdotL = max(dot(N, L), 0.0);
      float diffuse = NdotL * 0.65;
      float ambient = uAmbient.x;

      // Subtle distance fog
      float dist = length(vWorldPos);
      float fog = clamp(1.0 - dist / 50.0, 0.3, 1.0);

      vec3 lit = vColor * (ambient + diffuse) * fog;
      FragColor = vec4(clamp(lit, 0.0, 1.0), 1.0);
    }
    """;

    return renderer.CreateSpirVShader(
    [
      new() { Data = Encoding.UTF8.GetBytes(vertSrc), Type = ShaderProgramType.Vertex },
      new() { Data = Encoding.UTF8.GetBytes(fragSrc), Type = ShaderProgramType.Fragment },
    ]);
  }

  private static IShader CreatePortalQuadShader(IRenderer renderer)
  {
    string vertSrc =
    """
    #version 420 core
    layout(location = 0) in vec3 aPos;
    layout(location = 1) in vec2 aTexCoord;

    layout(std140) uniform PortalData {
      mat4 uMVP;
      vec4 uScreenSize;
    };

    void main()
    {
      gl_Position = uMVP * vec4(aPos, 1.0);
    }
    """;

    string fragSrc =
    """
    #version 420 core
    out vec4 FragColor;

    layout(std140) uniform PortalData {
      mat4 uMVP;
      vec4 uScreenSize;
    };

    layout(binding = 0) uniform sampler2D uPortalTex;

    void main()
    {
      // Screen-space UVs: sample the portal framebuffer at the current fragment's
      // screen position. Since the portal FB was rendered from the virtual camera
      // (same FOV/aspect), this gives correct parallax — the view through the
      // portal shifts as you move, like looking through a real window.
      vec2 screenUV = gl_FragCoord.xy / uScreenSize.xy;
      FragColor = texture(uPortalTex, screenUV);
    }
    """;

    return renderer.CreateSpirVShader(
    [
      new() { Data = Encoding.UTF8.GetBytes(vertSrc), Type = ShaderProgramType.Vertex },
      new() { Data = Encoding.UTF8.GetBytes(fragSrc), Type = ShaderProgramType.Fragment },
    ]);
  }

  private static IShader CreateCrosshairShader(IRenderer renderer)
  {
    string vertSrc =
    """
    #version 420 core
    layout(location = 0) in vec2 aPos;
    void main() { gl_Position = vec4(aPos, 0.0, 1.0); }
    """;

    string fragSrc =
    """
    #version 420 core
    out vec4 FragColor;
    void main() { FragColor = vec4(1.0, 1.0, 1.0, 0.7); }
    """;

    return renderer.CreateSpirVShader(
    [
      new() { Data = Encoding.UTF8.GetBytes(vertSrc), Type = ShaderProgramType.Vertex },
      new() { Data = Encoding.UTF8.GetBytes(fragSrc), Type = ShaderProgramType.Fragment },
    ]);
  }

  private static (Pipeline pipeline, CrosshairRes resources) CreateCrosshairPipeline(IRenderer renderer, IShader shader)
  {
    const float len = 0.02f;
    const float thick = 0.002f;
    float[] crossVerts =
    [
      -len, -thick, len, -thick, len, thick, -len, thick,
      -thick, -len, thick, -len, thick, len, -thick, len,
    ];
    uint[] crossInds = [0, 1, 2, 2, 3, 0, 4, 5, 6, 6, 7, 4];

    IBuffer vb = renderer.CreateBuffer(crossVerts.Length * sizeof(float));
    IBuffer ib = renderer.CreateBuffer(crossInds.Length * sizeof(uint));
    IBuffer ub = renderer.CreateBuffer(4);
    vb.Write<float>(crossVerts, 0);
    ib.Write<uint>(crossInds, 0);

    byte[] white = [255, 255, 255, 255];
    ITexture tex = renderer.CreateTexture2D(white, 1, 1, TextureFormat.RGBA8,
      TextureMagFilter.Nearest, TextureMinFilter.Nearest, TextureWrap.ClampToEdge);

    VertexAttribute[] attribs =
    [
      new() { Location = 0, Size = 2, Stride = 2 * sizeof(float), Offset = 0 },
    ];

    PipelineState state = new()
    {
      CullMode = CullMode.None,
      FillMode = FillMode.Solid,
      FrontFace = FrontFace.CounterClockwise,
      DepthFunc = DepthFunc.Always,
      DepthWrite = false,
      SrcBlendFactor = BlendFactor.SrcAlpha,
      DstBlendFactor = BlendFactor.OneMinusSrcAlpha,
      Topology = PrimitiveTopology.Triangles,
    };

    DrawEntry[] entries =
    [
      new(textures: [tex], vertexBuffer: vb, indexBuffer: ib, uniformBuffer: ub,
          vertexOffset: 0, indexStart: 0, indexCount: (uint)crossInds.Length,
          uniformStart: 0, uniformSize: 0),
    ];

    Pipeline pipeline = new(shader, state, attribs, entries);
    return (pipeline, new CrosshairRes(vb, ib, ub, tex));
  }

  private static void PrintControls()
  {
    Console.WriteLine(
    """
    === QuantiX: Non-Euclidean Portal Demo ===
    WASD          - move
    IJKL / arrows - look around
    H             - show controls
    Q / Esc       - quit

    Walk through doorways to teleport between
    rooms connected in impossible ways!
    ==========================================

    """);
  }
}

internal sealed class CrosshairRes(IBuffer vb, IBuffer ib, IBuffer ub, ITexture tex) : IDisposable
{
  public void Dispose()
  {
    vb.Dispose();
    ib.Dispose();
    ub.Dispose();
    tex.Dispose();
  }
}
