using System.Text;
using QuantiX.Engine.Audio;
using QuantiX.Engine.Mathematics.Vectors;
using QuantiX.Engine.Rendering;
using QuantiX.Engine.Windowing;
using QuantiX.Engine.Renderers.Quartz;

namespace QuantiX.Engine.Tests.Rendering;

public class MainServer : AppServer
{
  public WindowingService Windowing { get; } = new();
  public AudioService Audio { get; } = new(AudioFormat.Stereo16Bit);

  // ── Audio ──
  private readonly MusicSource _music = new(AudioFormat.Stereo16Bit);

  private const float DayCycleSecs = 120f;

  // Holds GPU buffers for one uploaded chunk (main-thread only)
  private sealed class ChunkGpu(IBuffer vb, IBuffer ib, uint count) : IDisposable
  {
    public readonly IBuffer Verts = vb;
    public readonly IBuffer Inds = ib;
    public readonly uint IndexCount = count;
    public void Dispose()
    {
      Verts.Dispose();
      Inds.Dispose();
    }
  }

  protected override void OnRun()
  {
    Window window = new("QuantiX Craft", 800, 600);

    this.Windowing.Server.Start();
    this.Windowing.Manager.Open(window);
    window.OnDestroy += this.Stop;

    // Start ambient music
    this.Audio.Player.Bind(_music);
    this.Audio.Server.Start();
    _music.Play();

    using IRenderer renderer = new QuartzRenderer();
    using IViewport viewport = renderer.CreateViewport(window);

    // World
    InfiniteWorld world = new();
    Camera cam = new() { Position = new(0, 60, 0), Yaw = 0, Pitch = -0.3f };
    BlockType selectedBlock = BlockType.Dirt;

    // World UBO: mat4 MVP (64) + mat4 lightMVP (64) + vec4 sunDir (16) + vec4 camPos (16) + vec4 lightParams (16) = 176 bytes
    using IBuffer uniformBuffer = renderer.CreateBuffer(176);

    // Shadow map
    const uint ShadowSize = 2048;
    using IFramebuffer shadowFB = renderer.CreateFramebuffer(ShadowSize, ShadowSize, TextureFormat.Depth32F);
    using IShader shadowShader = CreateShadowShader(renderer);
    using IBuffer shadowUB = renderer.CreateBuffer(64); // mat4 lightMVP only

    // Texture atlas
    byte[] atlasData = TextureAtlas.Generate();
    using ITexture texture = renderer.CreateTexture2D
    (
      atlasData, TextureAtlas.Width, TextureAtlas.Height,
      TextureFormat.RGBA8,
      TextureMagFilter.Nearest,
      TextureMinFilter.Nearest,
      TextureWrap.ClampToEdge
    );

    // ── Sky pipeline ──
    var (skyPipeline, skyResources) = CreateSkyPipeline(renderer);
    using var _sky = skyResources;

    // ── World pipeline state (DrawEntries rebuilt each frame) ──
    using IShader worldShader = CreateWorldShader(renderer);

    uint stride = 9 * sizeof(float);
    VertexAttribute[] worldAttribs =
    [
      new() { Location = 0, Size = 3, Stride = stride, Offset = 0 },
      new() { Location = 1, Size = 2, Stride = stride, Offset = 3 * sizeof(float) },
      new() { Location = 2, Size = 1, Stride = stride, Offset = 5 * sizeof(float) },
      new() { Location = 3, Size = 3, Stride = stride, Offset = 6 * sizeof(float) },
    ];

    // Shadow pass uses only position (location 0)
    VertexAttribute[] shadowAttribs =
    [
      new() { Location = 0, Size = 3, Stride = stride, Offset = 0 },
    ];

    PipelineState shadowState = new()
    {
      CullMode = CullMode.Front,
      FillMode = FillMode.Solid,
      FrontFace = FrontFace.CounterClockwise,
      DepthFunc = DepthFunc.Less,
      DepthWrite = true,
      SrcBlendFactor = BlendFactor.One,
      DstBlendFactor = BlendFactor.Zero,
      Topology = PrimitiveTopology.Triangles,
      DepthBiasConstant = 4.0f,
      DepthBiasSlope = 1.5f,
    };

    PipelineState worldState = new()
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

    // ── Crosshair pipeline ──
    var (crosshairPipeline, crosshairResources) = CreateCrosshairPipeline(renderer);
    using var _cs = crosshairResources;

    PrintControls();

    // Per-chunk GPU resources (main thread only)
    var gpuChunks = new Dictionary<ChunkEntry, ChunkGpu>();

    int frameCount = 0;
    long lastFpsTick = Environment.TickCount64;
    long startTick = Environment.TickCount64;

    while (this.State == AppServerState.Running)
    {
      frameCount++;
      long now = Environment.TickCount64;
      if (now - lastFpsTick >= 1000)
      {
        window.Title = $"QuanbloX [ {frameCount} FPS  |  {gpuChunks.Count} chunks ]";
        frameCount = 0;
        lastFpsTick = now;
      }

      while (Console.KeyAvailable)
      {
        ProcessInput(Console.ReadKey(true).Key, cam, world, ref selectedBlock);
      }

      // ── Infinite world update ──
      int camChunkX = (int)MathF.Floor(cam.Position.X / Chunk.SizeX);
      int camChunkZ = (int)MathF.Floor(cam.Position.Z / Chunk.SizeZ);
      world.Update(camChunkX, camChunkZ);

      // Upload newly meshed chunks
      while (world.TryDequeueReady(out var entry))
      {
        if (entry.Cancelled || entry.Inds!.Length == 0)
        {
          entry.Verts = null;
          entry.Inds = null;
          world.NotifyUploaded(entry);
          continue;
        }

        // Dispose old GPU data if this is a remesh
        if (gpuChunks.TryGetValue(entry, out var old))
        {
          old.Dispose();
          gpuChunks.Remove(entry);
        }

        var vb = renderer.CreateBuffer(entry.Verts!.Length * sizeof(float));
        var ib = renderer.CreateBuffer(entry.Inds.Length * sizeof(uint));
        vb.Write<float>(entry.Verts, 0);
        ib.Write<uint>(entry.Inds, 0);

        gpuChunks[entry] = new ChunkGpu(vb, ib, (uint)entry.Inds.Length);
        entry.Verts = null;
        entry.Inds = null;

        world.NotifyUploaded(entry);
      }

      // Dispose unloaded chunks
      while (world.TryDequeueUnload(out var entry))
      {
        if (gpuChunks.TryGetValue(entry, out var gpu))
        {
          gpu.Dispose();
          gpuChunks.Remove(entry);
        }
      }

      // ── Day/night cycle ──
      float elapsed = (now - startTick) / 1000f;
      float timeOfDay = (elapsed % DayCycleSecs) / DayCycleSecs;
      float sunAngle = timeOfDay * MathF.PI * 2f;
      float sunY = MathF.Sin(sunAngle);
      float sunX = MathF.Cos(sunAngle);
      float ambient = Math.Clamp(sunY * 1.5f + 0.3f, 0.12f, 1.0f);

      // Sun direction (same as sky shader)
      float sunLen = MathF.Sqrt(sunX * sunX + sunY * sunY + 0.3f * 0.3f);
      float sdx = sunX / sunLen, sdy = sunY / sunLen, sdz = 0.3f / sunLen;

      // ── Light-space matrix (orthographic from sun's perspective, centered on camera) ──
      const float shadowExtent = 120f;
      const float shadowDepth = 200f;
      var lightPos = new Vec3<float>(cam.Position.X + sdx * 80f, cam.Position.Y + sdy * 80f, cam.Position.Z + sdz * 80f);
      var lightFwd = new Vec3<float>(-sdx, -sdy, -sdz);

      // Build a stable right/up for the light
      var worldUp = MathF.Abs(lightFwd.Y) > 0.99f ? new Vec3<float>(0, 0, 1) : new Vec3<float>(0, 1, 0);
      var lightRightRaw = Vec3<float>.Cross(worldUp, lightFwd);
      float lrLen = MathF.Sqrt(lightRightRaw.X * lightRightRaw.X + lightRightRaw.Y * lightRightRaw.Y + lightRightRaw.Z * lightRightRaw.Z);
      var lightRight = new Vec3<float>(lightRightRaw.X / lrLen, lightRightRaw.Y / lrLen, lightRightRaw.Z / lrLen);
      var lightUp = Vec3<float>.Cross(lightFwd, lightRight);

      float[] lightView =
      [
        lightRight.X, lightUp.X, -lightFwd.X, 0,
        lightRight.Y, lightUp.Y, -lightFwd.Y, 0,
        lightRight.Z, lightUp.Z, -lightFwd.Z, 0,
        -Vec3<float>.Dot(lightRight, lightPos), -Vec3<float>.Dot(lightUp, lightPos), Vec3<float>.Dot(lightFwd, lightPos), 1,
      ];
      float[] lightProj = Mat4.Ortho(-shadowExtent, shadowExtent, -shadowExtent, shadowExtent, -shadowDepth, shadowDepth);
      float[] lightMVP = Mat4.Multiply(lightProj, lightView);

      // ── Build shadow draw entries (no textures needed, just depth) ──
      var shadowDrawEntries = new DrawEntry[gpuChunks.Count];
      var worldDrawEntries = new DrawEntry[gpuChunks.Count];
      int i = 0;
      foreach (var (_, gpu) in gpuChunks)
      {
        shadowDrawEntries[i] = new DrawEntry
        (
          textures: ReadOnlySpan<ITexture>.Empty,
          vertexBuffer: gpu.Verts,
          indexBuffer: gpu.Inds,
          uniformBuffer: shadowUB,
          vertexOffset: 0,
          indexStart: 0,
          indexCount: gpu.IndexCount,
          uniformStart: 0,
          uniformSize: 64
        );
        worldDrawEntries[i] = new DrawEntry
        (
          textures: (ITexture[])[texture, shadowFB.DepthTexture],
          vertexBuffer: gpu.Verts,
          indexBuffer: gpu.Inds,
          uniformBuffer: uniformBuffer,
          vertexOffset: 0,
          indexStart: 0,
          indexCount: gpu.IndexCount,
          uniformStart: 0,
          uniformSize: 176
        );
        i++;
      }

      // Shadow UBO
      shadowUB.Write<float>(lightMVP, 0);

      // World uniforms
      var (vpW, vpH) = viewport.Size;
      float aspect = vpH > 0 ? (float)vpW / vpH : 1f;
      float[] mvp = cam.GetViewProjection(aspect);
      uniformBuffer.Write<float>(mvp, 0);
      uniformBuffer.Write<float>(lightMVP, 64);
      uniformBuffer.Write<float>([sdx, sdy, sdz, 0], 128);
      uniformBuffer.Write<float>([cam.Position.X, cam.Position.Y, cam.Position.Z, 0], 144);
      uniformBuffer.Write<float>([ambient, 0, 0, 0], 160);

      // Sky uniforms
      float cloudOffset = elapsed * 0.02f;
      float tanHalfFov = MathF.Tan(Camera.Fov / 2f);
      var fwd = cam.Forward;
      var right = cam.Right;
      var up = cam.Up;

      Span<float> data =
      [
        timeOfDay, cloudOffset, tanHalfFov, aspect,
        right.X, right.Y, right.Z, 0,
        up.X, up.Y, up.Z, 0,
        fwd.X, fwd.Y, fwd.Z, 0
      ];

      skyResources.UniformBuffer.Write<float>(data, 0);

      // ── Shadow pass ──
      Pipeline shadowPipeline = new(shadowShader, shadowState, shadowAttribs, shadowDrawEntries);
      shadowFB.Draw([shadowPipeline]);

      // ── Main pass ──
      Pipeline worldPipeline = new(worldShader, worldState, worldAttribs, worldDrawEntries);
      viewport.Draw([skyPipeline, worldPipeline, crosshairPipeline]);
      viewport.Present();
      window.Update();
    }

    // Cleanup all GPU chunks on exit
    foreach (var gpu in gpuChunks.Values)
    {
      gpu.Dispose();
    }
  }

  private void ProcessInput(ConsoleKey key, Camera cam, InfiniteWorld world, ref BlockType selectedBlock)
  {
    switch (key)
    {
      case ConsoleKey.W: cam.MoveForward(); break;
      case ConsoleKey.S: cam.MoveBackward(); break;
      case ConsoleKey.D: cam.MoveRight(); break;
      case ConsoleKey.A: cam.MoveLeft(); break;
      case ConsoleKey.Spacebar: cam.MoveUp(); break;
      case ConsoleKey.C: cam.MoveDown(); break;
      case ConsoleKey.J: case ConsoleKey.LeftArrow: cam.LookLeft(); break;
      case ConsoleKey.L: case ConsoleKey.RightArrow: cam.LookRight(); break;
      case ConsoleKey.I: case ConsoleKey.UpArrow: cam.LookUp(); break;
      case ConsoleKey.K: case ConsoleKey.DownArrow: cam.LookDown(); break;

      case ConsoleKey.B:
        {
          var hit = world.Raycast(cam.Position, cam.Forward, 16f);
          if (hit.HasValue)
          {
            var (hx, hy, hz, _, _, _) = hit.Value;
            Console.WriteLine($"  Broke {world.GetBlock(hx, hy, hz)} at ({hx}, {hy}, {hz})");
            world.SetBlock(hx, hy, hz, BlockType.Air);
          }
          else Console.WriteLine("  No block in range");
          break;
        }

      case ConsoleKey.P:
        {
          var hit = world.Raycast(cam.Position, cam.Forward, 16f);
          if (hit.HasValue)
          {
            var (_, _, _, px, py, pz) = hit.Value;
            if (py >= 0)
            {
              world.SetBlock(px, py, pz, selectedBlock);
              Console.WriteLine($"  Placed {selectedBlock} at ({px}, {py}, {pz})");
            }
          }
          else Console.WriteLine("  No surface in range");
          break;
        }

      case ConsoleKey.D1:
        selectedBlock = BlockType.Grass;
        Console.WriteLine($"  Selected: {selectedBlock}");
        break;

      case ConsoleKey.D2:
        selectedBlock = BlockType.Dirt;
        Console.WriteLine($"  Selected: {selectedBlock}");
        break;

      case ConsoleKey.D3:
        selectedBlock = BlockType.Stone;
        Console.WriteLine($"  Selected: {selectedBlock}");
        break;

      case ConsoleKey.D4:
        selectedBlock = BlockType.Wood;
        Console.WriteLine($"  Selected: {selectedBlock}");
        break;

      case ConsoleKey.D5:
        selectedBlock = BlockType.Leaves;
        Console.WriteLine($"  Selected: {selectedBlock}");
        break;

      case ConsoleKey.D6:
        selectedBlock = BlockType.Sand;
        Console.WriteLine($"  Selected: {selectedBlock}");
        break;

      case ConsoleKey.D7:
        selectedBlock = BlockType.Snow;
        Console.WriteLine($"  Selected: {selectedBlock}");
        break;

      case ConsoleKey.D8:
        selectedBlock = BlockType.PineLog;
        Console.WriteLine($"  Selected: {selectedBlock}");
        break
        ;
      case ConsoleKey.D9:
        selectedBlock = BlockType.Water;
        Console.WriteLine($"  Selected: {selectedBlock}");
        break;

      case ConsoleKey.H:
        PrintControls();
        break;

      case ConsoleKey.Q:
      case ConsoleKey.Escape:
        this.Stop();
        break;
    }
  }

  // ── Shadow depth shader ──────────────────────────────────────────

  private static IShader CreateShadowShader(IRenderer renderer)
  {
    string vertSrc =
    """
    #version 330 core
    layout(location = 0) in vec3 aPos;

    layout(std140) uniform ShadowData {
      mat4 uLightMVP;
    };

    void main()
    {
      gl_Position = uLightMVP * vec4(aPos, 1.0);
    }
    """;

    string fragSrc =
    """
    #version 330 core
    void main() { }
    """;

    ShaderProgram vertProg = new()
    {
      Data = Encoding.UTF8.GetBytes(vertSrc),
      Type = ShaderProgramType.Vertex
    };
    ShaderProgram fragProg = new()
    {
      Data = Encoding.UTF8.GetBytes(fragSrc),
      Type = ShaderProgramType.Fragment
    };
    return renderer.CreateSpirVShader([vertProg, fragProg]);
  }

  // ── Lit world shader (directional + specular + shadow) ──────────

  private static IShader CreateWorldShader(IRenderer renderer)
  {
    string vertSrc =
    """
    #version 420 core
    layout(location = 0) in vec3 aPos;
    layout(location = 1) in vec2 aTexCoord;
    layout(location = 2) in float aBrightness;
    layout(location = 3) in vec3 aNormal;

    layout(std140) uniform WorldData {
      mat4 uMVP;
      mat4 uLightMVP;
      vec4 uSunDir;
      vec4 uCamPos;
      vec4 uLightParams;  // x = ambient
    };

    out vec2 vTexCoord;
    out float vBrightness;
    out vec3 vNormal;
    out vec3 vWorldPos;
    out vec4 vLightSpacePos;

    void main()
    {
      gl_Position = uMVP * vec4(aPos, 1.0);
      vTexCoord = aTexCoord;
      vBrightness = aBrightness;
      vNormal = aNormal;
      vWorldPos = aPos;
      vLightSpacePos = uLightMVP * vec4(aPos, 1.0);
    }
    """;

    string fragSrc =
    """
    #version 420 core
    in vec2 vTexCoord;
    in float vBrightness;
    in vec3 vNormal;
    in vec3 vWorldPos;
    in vec4 vLightSpacePos;
    out vec4 FragColor;

    layout(std140) uniform WorldData {
      mat4 uMVP;
      mat4 uLightMVP;
      vec4 uSunDir;
      vec4 uCamPos;
      vec4 uLightParams;
    };

    layout(binding = 0) uniform sampler2D uTexture;
    layout(binding = 1) uniform sampler2DShadow uShadowMap;

    float calcShadow(vec4 lsPos)
    {
      vec3 projCoords = lsPos.xyz / lsPos.w;
      projCoords = projCoords * 0.5 + 0.5;

      if (projCoords.z > 1.0) return 1.0;

      // PCF 3x3
      float shadow = 0.0;
      vec2 texelSize = 1.0 / vec2(textureSize(uShadowMap, 0));
      for (int x = -1; x <= 1; x++)
      {
        for (int y = -1; y <= 1; y++)
        {
          vec3 sampleCoord = vec3(projCoords.xy + vec2(x, y) * texelSize, projCoords.z);
          shadow += texture(uShadowMap, sampleCoord);
        }
      }
      return shadow / 9.0;
    }

    void main()
    {
      vec4 tex = texture(uTexture, vTexCoord);
      vec3 N = normalize(vNormal);
      vec3 L = normalize(uSunDir.xyz);
      vec3 V = normalize(uCamPos.xyz - vWorldPos);
      vec3 H = normalize(L + V);

      float ambient = uLightParams.x;

      // Diffuse (Lambert)
      float NdotL = max(dot(N, L), 0.0);
      float diffuse = NdotL * 0.6;

      // Specular (Blinn-Phong)
      float NdotH = max(dot(N, H), 0.0);
      float specular = pow(NdotH, 32.0) * 0.15 * step(0.01, NdotL);

      // Shadow (only when sun is up)
      float sunUp = smoothstep(-0.05, 0.1, uSunDir.y);
      float shadow = calcShadow(vLightSpacePos);
      shadow = mix(1.0, shadow, sunUp);

      float lighting = ambient + (diffuse + specular) * shadow;
      lighting *= vBrightness;

      FragColor = vec4(tex.rgb * clamp(lighting, 0.0, 1.4), tex.a);
    }
    """;

    ShaderProgram vertProg = new()
    {
      Data = Encoding.UTF8.GetBytes(vertSrc),
      Type = ShaderProgramType.Vertex
    };
    ShaderProgram fragProg = new()
    {
      Data = Encoding.UTF8.GetBytes(fragSrc),
      Type = ShaderProgramType.Fragment
    };
    return renderer.CreateSpirVShader([vertProg, fragProg]);
  }

  // ── Sky pipeline (proper 3D skybox using camera direction vectors) ──

  private static (Pipeline pipeline, SkyResources resources) CreateSkyPipeline(IRenderer renderer)
  {
    string vertSrc =
    """
    #version 330 core
    layout(location = 0) in vec2 aPos;
    out vec2 vPos;

    void main()
    {
      vPos = aPos;
      gl_Position = vec4(aPos, 1.0, 1.0);
    }
    """;

    string fragSrc =
    """
    #version 330 core
    in vec2 vPos;
    out vec4 FragColor;

    layout(std140) uniform SkyData {
      vec4 uSkyParams;   // x=timeOfDay, y=cloudOffset, z=tanHalfFov, w=aspect
      vec4 uCamRight;    // xyz = camera right
      vec4 uCamUp;       // xyz = camera up
      vec4 uCamForward;  // xyz = camera forward
    };

    float hash(vec2 p)
    {
      return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453);
    }

    float noise(vec2 p)
    {
      vec2 i = floor(p);
      vec2 f = fract(p);
      f = f * f * (3.0 - 2.0 * f);
      return mix(
        mix(hash(i), hash(i + vec2(1,0)), f.x),
        mix(hash(i + vec2(0,1)), hash(i + vec2(1,1)), f.x),
        f.y);
    }

    float fbm(vec2 p)
    {
      float v = 0.0, a = 0.5;
      for (int i = 0; i < 4; i++) { v += a * noise(p); p *= 2.0; a *= 0.5; }
      return v;
    }

    void main()
    {
      float timeOfDay = uSkyParams.x;
      float cloudOff  = uSkyParams.y;
      float tanFov    = uSkyParams.z;
      float aspect    = uSkyParams.w;

      // Reconstruct world-space view direction from screen position
      vec3 dir = normalize(
        uCamForward.xyz
        + uCamRight.xyz * vPos.x * tanFov * aspect
        + uCamUp.xyz    * vPos.y * tanFov
      );

      // Sun world-space direction (orbits in XY plane)
      float sunAngle = timeOfDay * 6.28318;
      vec3 sunDir = normalize(vec3(cos(sunAngle), sin(sunAngle), 0.3));

      float dayFactor = smoothstep(-0.1, 0.2, sunDir.y);

      // Sky gradient based on world-space up
      float h = clamp(dir.y * 0.8 + 0.5, 0.0, 1.0);

      vec3 dayZenith   = vec3(0.25, 0.47, 0.92);
      vec3 dayHorizon  = vec3(0.60, 0.75, 0.95);
      vec3 nightZenith = vec3(0.01, 0.01, 0.05);
      vec3 nightHorizon= vec3(0.04, 0.04, 0.12);

      vec3 sky = mix(
        mix(nightHorizon, nightZenith, h),
        mix(dayHorizon, dayZenith, h),
        dayFactor);

      // Sunrise/sunset glow near horizon
      float sunsetFactor = smoothstep(-0.15, 0.0, sunDir.y) * smoothstep(0.3, 0.0, sunDir.y);
      float horizonGlow = pow(1.0 - abs(dir.y), 4.0);
      vec3 sunsetColor = mix(vec3(1.0, 0.3, 0.1), vec3(1.0, 0.6, 0.2), h);
      sky = mix(sky, sunsetColor, sunsetFactor * horizonGlow * 0.8);

      // Sun disk (world-space dot product)
      float sunDot = dot(dir, sunDir);
      float sunDisk = smoothstep(0.998, 0.999, sunDot);
      float sunGlow = pow(max(sunDot, 0.0), 64.0) * 0.4;
      vec3 sunColor = mix(vec3(1.0, 0.8, 0.4), vec3(1.0, 0.95, 0.8), dayFactor);
      sky += sunColor * (sunDisk + sunGlow) * step(-0.1, sunDir.y);

      // Moon (opposite side)
      vec3 moonDir = -sunDir;
      float moonDot = dot(dir, moonDir);
      float moonDisk = smoothstep(0.998, 0.9995, moonDot);
      float moonGlow = pow(max(moonDot, 0.0), 128.0) * 0.2;
      sky += vec3(0.8, 0.85, 0.95) * (moonDisk + moonGlow) * (1.0 - dayFactor);

      // Stars (in world-space, fixed positions)
      if (dayFactor < 0.5 && dir.y > 0.0)
      {
        // Project onto a sphere for stable star positions
        vec2 starUV = vec2(atan(dir.z, dir.x), asin(dir.y)) * 10.0;
        float star = hash(floor(starUV));
        star = step(0.97, star) * (1.0 - dayFactor * 2.0);
        star *= 0.5 + 0.5 * sin(star * 100.0 + cloudOff * 50.0);
        sky += vec3(star * dir.y);
      }

      // Clouds (projected onto a dome using world direction)
      if (dir.y > 0.02)
      {
        // Project ray onto a flat cloud layer at a fixed height
        vec2 cloudUV = dir.xz / (dir.y + 0.1) * 0.3;
        cloudUV.x += cloudOff;
        float cloud = fbm(cloudUV * 4.0);
        cloud = smoothstep(0.35, 0.65, cloud);

        vec3 cloudColor = mix(vec3(0.05, 0.06, 0.1), vec3(1.0), dayFactor);
        cloudColor = mix(cloudColor, vec3(1.0, 0.6, 0.3), sunsetFactor * 0.5);

        float fadeEdge = smoothstep(0.02, 0.15, dir.y);
        sky = mix(sky, cloudColor, cloud * 0.7 * fadeEdge);
      }

      // Below horizon: fade to fog
      if (dir.y < 0.0)
      {
        float belowFactor = smoothstep(0.0, -0.3, dir.y);
        vec3 fogColor = mix(dayHorizon, nightHorizon, 1.0 - dayFactor) * 0.4;
        sky = mix(sky, fogColor, belowFactor);
      }

      FragColor = vec4(clamp(sky, 0.0, 1.0), 1.0);
    }
    """;

    ShaderProgram vertProg = new()
    {
      Data = Encoding.UTF8.GetBytes(vertSrc),
      Type = ShaderProgramType.Vertex
    };
    ShaderProgram fragProg = new()
    {
      Data = Encoding.UTF8.GetBytes(fragSrc),
      Type = ShaderProgramType.Fragment
    };
    IShader shader = renderer.CreateSpirVShader([vertProg, fragProg]);

    // Fullscreen triangle
    float[] skyVerts = [-1, -1, 3, -1, -1, 3];
    uint[] skyInds = [0, 1, 2];

    IBuffer vb = renderer.CreateBuffer(skyVerts.Length * sizeof(float));
    IBuffer ib = renderer.CreateBuffer(skyInds.Length * sizeof(uint));
    // Sky UBO: 4 x vec4 = 64 bytes
    IBuffer ub = renderer.CreateBuffer(64);

    vb.Write<float>(skyVerts, 0);
    ib.Write<uint>(skyInds, 0);

    byte[] white = [255, 255, 255, 255];
    ITexture tex = renderer.CreateTexture2D(
      white, 1, 1,
      TextureFormat.RGBA8,
      TextureMagFilter.Nearest,
      TextureMinFilter.Nearest,
      TextureWrap.ClampToEdge
    );

    VertexAttribute[] attribs =
    [
      new() { Location = 0, Size = 2, Stride = 2 * sizeof(float), Offset = 0 },
    ];

    PipelineState state = new()
    {
      CullMode = CullMode.None,
      FillMode = FillMode.Solid,
      FrontFace = FrontFace.CounterClockwise,
      DepthFunc = DepthFunc.LessOrEqual,
      DepthWrite = false,
      SrcBlendFactor = BlendFactor.One,
      DstBlendFactor = BlendFactor.Zero,
      Topology = PrimitiveTopology.Triangles,
    };

    DrawEntry[] drawEntries =
    [
      new(
        textures: [tex],
        vertexBuffer: vb,
        indexBuffer: ib,
        uniformBuffer: ub,
        vertexOffset: 0,
        indexStart: 0,
        indexCount: 3,
        uniformStart: 0,
        uniformSize: 64
      )
    ];

    Pipeline pipeline = new(shader, state, attribs, drawEntries);
    return (pipeline, new SkyResources(shader, vb, ib, ub, tex));
  }

  // ── Crosshair pipeline ────────────────────────────────────────────

  private static (Pipeline pipeline, CrosshairResources resources) CreateCrosshairPipeline(IRenderer renderer)
  {
    string vertSrc =
    """
    #version 330 core
    layout(location = 0) in vec2 aPos;
    void main() { gl_Position = vec4(aPos, 0.0, 1.0); }
    """;

    string fragSrc =
    """
    #version 330 core
    out vec4 FragColor;
    void main() { FragColor = vec4(1.0, 1.0, 1.0, 0.8); }
    """;

    ShaderProgram vertProg = new()
    {
      Data = Encoding.UTF8.GetBytes(vertSrc),
      Type = ShaderProgramType.Vertex
    };
    ShaderProgram fragProg = new()
    {
      Data = Encoding.UTF8.GetBytes(fragSrc),
      Type = ShaderProgramType.Fragment
    };
    IShader shader = renderer.CreateSpirVShader([vertProg, fragProg]);

    const float len = 0.025f;
    const float thick = 0.003f;
    float[] crossVerts =
    [
      -len, -thick,  len, -thick,  len, thick,  -len, thick,
      -thick, -len,  thick, -len,  thick, len,  -thick, len,
    ];
    uint[] crossInds = [0, 1, 2, 2, 3, 0, 4, 5, 6, 6, 7, 4];

    IBuffer vb = renderer.CreateBuffer(crossVerts.Length * sizeof(float));
    IBuffer ib = renderer.CreateBuffer(crossInds.Length * sizeof(uint));
    IBuffer ub = renderer.CreateBuffer(4);

    vb.Write<float>(crossVerts, 0);
    ib.Write<uint>(crossInds, 0);

    byte[] white = [255, 255, 255, 255];
    ITexture tex = renderer.CreateTexture2D(
      white, 1, 1,
      TextureFormat.RGBA8,
      TextureMagFilter.Nearest,
      TextureMinFilter.Nearest,
      TextureWrap.ClampToEdge
    );

    VertexAttribute[] attribs =
    [
      new()
      {
        Location = 0,
        Size = 2,
        Stride = 2 * sizeof(float), Offset = 0
      },
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

    DrawEntry[] drawEntries =
    [
      new
      (
        textures: [tex],
        vertexBuffer: vb,
        indexBuffer: ib,
        uniformBuffer: ub,
        vertexOffset: 0,
        indexStart: 0,
        indexCount: (uint)crossInds.Length,
        uniformStart: 0,
        uniformSize: 0
      )
    ];

    Pipeline pipeline = new(shader, state, attribs, drawEntries);
    return (pipeline, new CrosshairResources(shader, vb, ib, ub, tex));
  }

  private static void PrintControls()
  {
    Console.WriteLine(
      """
      === QuantiX Craft (Infinite World) ===
      WASD      - move horizontal
      Space     - up    C - down
      IJKL / arrows - look around
      B - break block   P - place block
      1-5 - Grass/Dirt/Stone/Wood/Leaves
      6-9 - Sand/Snow/PineLog/Water
      H - show controls  Q/Esc - quit
      =======================================

      """
    );
  }
}

internal sealed class SkyResources(IShader shader, IBuffer vb, IBuffer ib, IBuffer ub, ITexture tex) : IDisposable
{
  public IBuffer UniformBuffer => ub;

  public void Dispose()
  {
    shader.Dispose();
    vb.Dispose();
    ib.Dispose();
    ub.Dispose();
    tex.Dispose();
  }
}

internal sealed class CrosshairResources(IShader shader, IBuffer vb, IBuffer ib, IBuffer ub, ITexture tex) : IDisposable
{
  public void Dispose()
  {
    shader.Dispose();
    vb.Dispose();
    ib.Dispose();
    ub.Dispose();
    tex.Dispose();
  }
}
