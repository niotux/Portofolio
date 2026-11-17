using System.Text;
using QuantiX.Engine.Audio;
using QuantiX.Engine.Mathematics.Vectors;
using QuantiX.Engine.Rendering;
using QuantiX.Engine.Renderers.Quartz;
using QuantiX.Engine.Windowing;

namespace QuantiX.Engine.Tests.FPS;

public enum GameState { MainMenu, Playing, BuyMenu, GameOver }

public class GameServer : AppServer
{
  public WindowingService Windowing { get; } = new();
  public AudioService Audio { get; } = new(AudioFormat.Stereo16Bit);

  private const float DayCycleSecs = 180f;

  // ── Game state ──
  private GameState _state = GameState.MainMenu;
  private int _score;
  private float _health;
  private const float MaxHealth = 100f;
  private int _selectedMenuItem;
  private int _selectedBuyItem;
  private readonly Weapon[] _weapons = [Weapon.CreatePistol(), Weapon.CreateRifle(), Weapon.CreateShotgun()];
  private int _currentWeapon;
  private readonly bool[] _weaponOwned = [true, false, false];
  private float _hitMarkerTimer;
  private float _damageFlashTimer;

  // ── World ──
  private Arena _arena = new();
  private readonly List<Enemy> _enemies = new();
  private WaveManager _waveManager = new();
  private readonly Random _rng = new();
  private Camera _cam = new();
  private float _velocityY;
  private bool _onGround;
  private readonly InputState _input = new();

  // ── Audio ──
  private readonly MusicSource _music = new(AudioFormat.Stereo16Bit);

  // ── Particles ──
  private readonly List<Particle> _particles = new();

  protected override void OnRun()
  {
    Window window = new("QuantiX: Breach Protocol", 1280, 720);

    this.Audio.Player.Bind(_music);
    this.Audio.Server.Start();

    this.Windowing.Server.Start();
    this.Windowing.Manager.Open(window);
    window.OnDestroy += this.Stop;

    using IRenderer renderer = new QuartzRenderer();
    using IViewport viewport = renderer.CreateViewport(window);

    // Generate arena
    _arena.Generate();
    Console.WriteLine($"Arena: {_arena.Vertices.Length / 9} vertices, {_arena.Indices.Length / 3} triangles, {_arena.StaticColliders.Count} colliders, {_arena.GrassPositions.Count} grass blades");

    // Texture atlas
    byte[] atlasData = TextureGen.Generate();
    using ITexture atlas = renderer.CreateTexture2D(
      atlasData, TextureGen.AtlasSize, TextureGen.AtlasSize,
      TextureFormat.RGBA8, TextureMagFilter.Nearest, TextureMinFilter.Nearest, TextureWrap.Repeat);

    // White texture for untextured rendering
    byte[] whiteData = [255, 255, 255, 255];
    using ITexture whiteTex = renderer.CreateTexture2D(
      whiteData, 1, 1, TextureFormat.RGBA8, TextureMagFilter.Nearest, TextureMinFilter.Nearest, TextureWrap.ClampToEdge);

    // ── Upload arena geometry ──
    using IBuffer arenaVB = renderer.CreateBuffer(_arena.Vertices.Length * sizeof(float));
    using IBuffer arenaIB = renderer.CreateBuffer(_arena.Indices.Length * sizeof(uint));
    arenaVB.Write<float>(_arena.Vertices, 0);
    arenaIB.Write<uint>(_arena.Indices, 0);

    // ── Sky pipeline ──
    var (skyPipeline, skyUB) = CreateSkyPipeline(renderer, whiteTex);

    // ── World uniform: MVP(64) + Model(64) + lighting(16) + camPos(16) + fog(16) = 176 bytes ──
    const int WorldUBOSize = 176;
    using IBuffer worldUB = renderer.CreateBuffer(WorldUBOSize);

    uint worldStride = 9 * sizeof(float);
    VertexAttribute[] worldAttribs =
    [
      new() { Location = 0, Size = 3, Stride = worldStride, Offset = 0 },
      new() { Location = 1, Size = 3, Stride = worldStride, Offset = 3 * sizeof(float) },
      new() { Location = 2, Size = 2, Stride = worldStride, Offset = 6 * sizeof(float) },
      new() { Location = 3, Size = 1, Stride = worldStride, Offset = 8 * sizeof(float) },
    ];

    using IShader worldShader = CreateShader(renderer, Shaders.WorldVert, Shaders.WorldFrag);

    PipelineState worldPipeState = new()
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

    // ── Enemy mesh (humanoid, pre-built) ──
    var enemyMB = new MeshBuilder();
    var eTex = TextureGen.EnemyRed;
    enemyMB.AddHumanoid(eTex.uMin, eTex.vMin, eTex.uMax, eTex.vMax);
    float[] enemyVerts = enemyMB.GetVertices();
    uint[] enemyInds = enemyMB.GetIndices();

    using IBuffer enemyVB = renderer.CreateBuffer(enemyVerts.Length * sizeof(float));
    using IBuffer enemyIB = renderer.CreateBuffer(enemyInds.Length * sizeof(uint));
    enemyVB.Write<float>(enemyVerts, 0);
    enemyIB.Write<uint>(enemyInds, 0);
    uint enemyIndexCount = (uint)enemyInds.Length;

    // Enemy UBOs (pool of 64)
    const int MaxEnemies = 64;
    IBuffer[] enemyUBs = new IBuffer[MaxEnemies];
    for (int i = 0; i < MaxEnemies; i++)
      enemyUBs[i] = renderer.CreateBuffer(WorldUBOSize);

    // ── Grass pipeline ──
    var (grassVerts, grassInds, grassBladeCount) = BuildGrassMesh(_arena.GrassPositions);
    using IBuffer grassVB = renderer.CreateBuffer(grassVerts.Length * sizeof(float));
    using IBuffer grassIB = renderer.CreateBuffer(grassInds.Length * sizeof(uint));
    grassVB.Write<float>(grassVerts, 0);
    grassIB.Write<uint>(grassInds, 0);

    // Grass UBO: VP(64) + wind(16) + camPos(16) = 96
    using IBuffer grassUB = renderer.CreateBuffer(96);
    using IShader grassShader = CreateShader(renderer, Shaders.GrassVert, Shaders.GrassFrag);

    VertexAttribute[] grassAttribs =
    [
      new() { Location = 0, Size = 3, Stride = 7 * sizeof(float), Offset = 0 },
      new() { Location = 1, Size = 2, Stride = 7 * sizeof(float), Offset = 3 * sizeof(float) },
      new() { Location = 2, Size = 2, Stride = 7 * sizeof(float), Offset = 5 * sizeof(float) },
    ];

    PipelineState grassState = new()
    {
      CullMode = CullMode.None,
      FillMode = FillMode.Solid,
      FrontFace = FrontFace.CounterClockwise,
      DepthFunc = DepthFunc.Less,
      DepthWrite = true,
      SrcBlendFactor = BlendFactor.SrcAlpha,
      DstBlendFactor = BlendFactor.OneMinusSrcAlpha,
      Topology = PrimitiveTopology.Triangles,
    };

    // ── Particle pipeline ──
    const int MaxParticleVerts = 4000 * 4; // 4000 particles max, 4 verts each
    const int MaxParticleInds = 4000 * 6;
    using IBuffer particleVB = renderer.CreateBuffer(MaxParticleVerts * 9 * sizeof(float));
    using IBuffer particleIB = renderer.CreateBuffer(MaxParticleInds * sizeof(uint));
    using IBuffer particleUB = renderer.CreateBuffer(64); // just VP matrix
    using IShader particleShader = CreateShader(renderer, Shaders.ParticleVert, Shaders.ParticleFrag);

    VertexAttribute[] particleAttribs =
    [
      new() { Location = 0, Size = 3, Stride = 9 * sizeof(float), Offset = 0 },
      new() { Location = 1, Size = 2, Stride = 9 * sizeof(float), Offset = 3 * sizeof(float) },
      new() { Location = 2, Size = 4, Stride = 9 * sizeof(float), Offset = 5 * sizeof(float) },
    ];

    PipelineState particleState = new()
    {
      CullMode = CullMode.None,
      FillMode = FillMode.Solid,
      FrontFace = FrontFace.CounterClockwise,
      DepthFunc = DepthFunc.Less,
      DepthWrite = false,
      SrcBlendFactor = BlendFactor.SrcAlpha,
      DstBlendFactor = BlendFactor.One, // additive blending
      Topology = PrimitiveTopology.Triangles,
    };

    // ── HUD pipeline ──
    const int MaxHudVerts = 2000;
    const int MaxHudInds = 3000;
    using IBuffer hudVB = renderer.CreateBuffer(MaxHudVerts * 8 * sizeof(float));
    using IBuffer hudIB = renderer.CreateBuffer(MaxHudInds * sizeof(uint));
    using IBuffer hudUB = renderer.CreateBuffer(4);
    using IShader hudShader = CreateShader(renderer, Shaders.HudVert, Shaders.HudFrag);

    VertexAttribute[] hudAttribs =
    [
      new() { Location = 0, Size = 2, Stride = 8 * sizeof(float), Offset = 0 },
      new() { Location = 1, Size = 2, Stride = 8 * sizeof(float), Offset = 2 * sizeof(float) },
      new() { Location = 2, Size = 4, Stride = 8 * sizeof(float), Offset = 4 * sizeof(float) },
    ];

    PipelineState hudState = new()
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

    PrintControls();
    ResetGame();

    int frameCount = 0;
    long lastFpsTick = Environment.TickCount64;
    long lastFrameTick = Environment.TickCount64;
    long startTick = Environment.TickCount64;

    _music.Play();
    while (this.State == AppServerState.Running)
    {
      long now = Environment.TickCount64;
      float dt = Math.Min((now - lastFrameTick) / 1000f, 0.1f);
      lastFrameTick = now;

      frameCount++;
      if (now - lastFpsTick >= 1000)
      {
        window.Title = $"QuantiX: Breach Protocol [ {frameCount} FPS | Wave {_waveManager.CurrentWave} | {_enemies.Count(e => e.State == EnemyState.Active)} enemies ]";
        frameCount = 0;
        lastFpsTick = now;
      }

      // ── Input: feed all pending keypresses into InputState ──
      while (Console.KeyAvailable)
      {
        var key = Console.ReadKey(true).Key;
        _input.KeyPressed(key);
        ProcessInput(key);
      }
      _input.Update(dt);

      // ── Update ──
      Update(dt);

      // ── Build pipelines ──
      var (vpW, vpH) = viewport.Size;
      float aspect = vpH > 0 ? (float)vpW / vpH : 1f;
      float elapsed = (now - startTick) / 1000f;
      float timeOfDay = (elapsed % DayCycleSecs) / DayCycleSecs;

      // Sky uniforms
      float cloudOffset = elapsed * 0.02f;
      float tanHalfFov = MathF.Tan(Camera.Fov / 2f);
      var fwd = _cam.Forward;
      var right = _cam.Right;
      var up = _cam.Up;
      Span<float> skyData =
      [
        timeOfDay, cloudOffset, tanHalfFov, aspect,
        right.X, right.Y, right.Z, 0,
        up.X, up.Y, up.Z, 0,
        fwd.X, fwd.Y, fwd.Z, 0
      ];
      skyUB.Write<float>(skyData, 0);

      // World uniforms (arena = identity model matrix)
      float[] vp = _cam.GetViewProjection(aspect);
      float[] identity = Mat4.Identity();
      float sunY = MathF.Sin(timeOfDay * MathF.Tau);
      float ambient = Math.Clamp(sunY * 1.5f + 0.3f, 0.15f, 1.0f);

      float[] worldData = new float[44];
      vp.AsSpan().CopyTo(worldData);
      identity.AsSpan().CopyTo(worldData[16..]);
      worldData[32] = ambient; worldData[33] = timeOfDay; worldData[34] = 32f; worldData[35] = 0;
      worldData[36] = _cam.Position.X; worldData[37] = _cam.Position.Y; worldData[38] = _cam.Position.Z; worldData[39] = 0;
      worldData[40] = 40f; worldData[41] = 150f; worldData[42] = 0; worldData[43] = 0;
      worldUB.Write<float>(worldData, 0);

      DrawEntry arenaDrawEntry = new(
        textures: (ITexture[])[atlas],
        vertexBuffer: arenaVB,
        indexBuffer: arenaIB,
        uniformBuffer: worldUB,
        vertexOffset: 0, indexStart: 0, indexCount: (uint)_arena.Indices.Length,
        uniformStart: 0, uniformSize: WorldUBOSize);

      Pipeline worldPipeline = new(worldShader, worldPipeState, worldAttribs, [arenaDrawEntry]);

      // ── Enemy draw entries ──
      var enemyDrawEntries = new List<DrawEntry>();
      int ei = 0;
      foreach (var enemy in _enemies)
      {
        if (enemy.State == EnemyState.Dead || ei >= MaxEnemies) continue;

        float[] model = Mat4.TRS(enemy.Position, enemy.RotationY, new Vec3<float>(1, 1, 1));
        float[] mvp = Mat4.Multiply(vp, model);

        // Color tint based on enemy type + damage flash
        float tintPacked = 0;
        if (enemy.DamageFlash > 0)
          tintPacked = 255 * 65536 + 50 * 256 + 50; // red flash
        else if (enemy.Type == EnemyType.Tank)
          tintPacked = 80 * 65536 + 80 * 256 + 200; // blue
        else if (enemy.Type == EnemyType.Prop && !enemy.IsPropRevealed)
          tintPacked = 160 * 65536 + 120 * 256 + 60; // crate color
        else if (enemy.Type == EnemyType.Prop)
          tintPacked = 50 * 65536 + 180 * 256 + 50; // green when revealed

        float[] eData = new float[44];
        mvp.AsSpan().CopyTo(eData);
        model.AsSpan().CopyTo(eData[16..]);
        eData[32] = ambient; eData[33] = timeOfDay; eData[34] = 16f; eData[35] = tintPacked;
        eData[36] = _cam.Position.X; eData[37] = _cam.Position.Y; eData[38] = _cam.Position.Z; eData[39] = 0;
        eData[40] = 40f; eData[41] = 150f; eData[42] = 0; eData[43] = 0;
        enemyUBs[ei].Write<float>(eData, 0);

        enemyDrawEntries.Add(new DrawEntry(
          textures: (ITexture[])[atlas],
          vertexBuffer: enemyVB,
          indexBuffer: enemyIB,
          uniformBuffer: enemyUBs[ei],
          vertexOffset: 0, indexStart: 0, indexCount: enemyIndexCount,
          uniformStart: 0, uniformSize: WorldUBOSize));
        ei++;
      }

      Pipeline enemyPipeline = new(worldShader, worldPipeState, worldAttribs, enemyDrawEntries.ToArray());

      // ── Grass uniforms ──
      float[] grassData = new float[24];
      vp.AsSpan().CopyTo(grassData);
      grassData[16] = elapsed; grassData[17] = 0.15f; grassData[18] = 0; grassData[19] = 0;
      grassData[20] = _cam.Position.X; grassData[21] = _cam.Position.Y; grassData[22] = _cam.Position.Z; grassData[23] = 0;
      grassUB.Write<float>(grassData, 0);

      DrawEntry grassDraw = new(
        textures: (ITexture[])[whiteTex],
        vertexBuffer: grassVB,
        indexBuffer: grassIB,
        uniformBuffer: grassUB,
        vertexOffset: 0, indexStart: 0, indexCount: (uint)grassInds.Length,
        uniformStart: 0, uniformSize: 96);

      Pipeline grassPipeline = new(grassShader, grassState, grassAttribs, [grassDraw]);

      // ── Particles ──
      var (pVerts, pInds, pCount) = BuildParticleMesh(_particles, _cam);
      uint particleIndexCount = 0;
      if (pCount > 0)
      {
        particleVB.Write<float>(pVerts.AsSpan(0, pCount * 4 * 9), 0);
        particleIB.Write<uint>(pInds.AsSpan(0, pCount * 6), 0);
        particleUB.Write<float>(vp, 0);
        particleIndexCount = (uint)(pCount * 6);
      }

      DrawEntry particleDraw = new(
        textures: (ITexture[])[whiteTex],
        vertexBuffer: particleVB,
        indexBuffer: particleIB,
        uniformBuffer: particleUB,
        vertexOffset: 0, indexStart: 0, indexCount: particleIndexCount,
        uniformStart: 0, uniformSize: 64);

      Pipeline particlePipeline = new(particleShader, particleState, particleAttribs, [particleDraw]);

      // ── HUD ──
      var (hVerts, hInds, hCount) = BuildHUD(aspect);
      uint hudIndexCount = 0;
      if (hCount > 0)
      {
        hudVB.Write<float>(hVerts.AsSpan(0, hCount * 4 * 8), 0);
        hudIB.Write<uint>(hInds.AsSpan(0, hCount * 6), 0);
        hudIndexCount = (uint)(hCount * 6);
      }

      DrawEntry hudDraw = new(
        textures: (ITexture[])[whiteTex],
        vertexBuffer: hudVB,
        indexBuffer: hudIB,
        uniformBuffer: hudUB,
        vertexOffset: 0, indexStart: 0, indexCount: hudIndexCount,
        uniformStart: 0, uniformSize: 0);

      Pipeline hudPipeline = new(hudShader, hudState, hudAttribs, [hudDraw]);

      // ── Render ──
      List<Pipeline> pipelines = [skyPipeline, worldPipeline, enemyPipeline, grassPipeline];
      if (particleIndexCount > 0) pipelines.Add(particlePipeline);
      pipelines.Add(hudPipeline);

      viewport.Draw(pipelines.ToArray());
      viewport.Present();
      window.Update();
    }

    // Cleanup
    foreach (var ub in enemyUBs) ub.Dispose();
  }

  // ═══════════════════════════════════════════════════════════════════
  // INPUT
  // ═══════════════════════════════════════════════════════════════════

  private void ProcessInput(ConsoleKey key)
  {
    switch (_state)
    {
      case GameState.MainMenu:
        HandleMenuInput(key);
        break;
      case GameState.Playing:
        HandleGameAction(key); // one-shot actions only
        break;
      case GameState.BuyMenu:
        HandleBuyInput(key);
        break;
      case GameState.GameOver:
        if (key == ConsoleKey.Enter || key == ConsoleKey.Escape)
        {
          _state = GameState.MainMenu;
          _selectedMenuItem = 0;
        }
        break;
    }
  }

  private void HandleMenuInput(ConsoleKey key)
  {
    switch (key)
    {
      case ConsoleKey.UpArrow or ConsoleKey.I:
        _selectedMenuItem = Math.Max(0, _selectedMenuItem - 1);
        break;
      case ConsoleKey.DownArrow or ConsoleKey.K:
        _selectedMenuItem = Math.Min(1, _selectedMenuItem + 1);
        break;
      case ConsoleKey.Enter:
        if (_selectedMenuItem == 0) // Play
        {
          ResetGame();
          _state = GameState.Playing;
          _waveManager.StartNextWave(_arena.EnemySpawnPoints, _rng);
          Console.WriteLine("=== WAVE 1 START ===");
        }
        else // Quit
          this.Stop();
        break;
      case ConsoleKey.Q or ConsoleKey.Escape:
        this.Stop();
        break;
    }
  }

  /// <summary>One-shot actions (not movement/look — those are continuous in Update).</summary>
  private void HandleGameAction(ConsoleKey key)
  {
    switch (key)
    {
      case ConsoleKey.F: TryShoot(); break;
      case ConsoleKey.R: _weapons[_currentWeapon].Reload(); break;
      case ConsoleKey.D1:
        if (_weaponOwned[0]) { _currentWeapon = 0; Console.WriteLine("  Pistol"); }
        break;
      case ConsoleKey.D2:
        if (_weaponOwned[1]) { _currentWeapon = 1; Console.WriteLine("  Rifle"); }
        break;
      case ConsoleKey.D3:
        if (_weaponOwned[2]) { _currentWeapon = 2; Console.WriteLine("  Shotgun"); }
        break;
      case ConsoleKey.B:
        if (!_waveManager.WaveActive && _waveManager.BuyPhase)
          _state = GameState.BuyMenu;
        break;
      case ConsoleKey.H: PrintControls(); break;
      case ConsoleKey.Q or ConsoleKey.Escape: _state = GameState.MainMenu; break;
    }
  }

  private void HandleBuyInput(ConsoleKey key)
  {
    switch (key)
    {
      case ConsoleKey.UpArrow or ConsoleKey.I:
        _selectedBuyItem = Math.Max(0, _selectedBuyItem - 1);
        break;
      case ConsoleKey.DownArrow or ConsoleKey.K:
        _selectedBuyItem = Math.Min(4, _selectedBuyItem + 1);
        break;
      case ConsoleKey.Enter:
        TryBuy(_selectedBuyItem);
        break;
      case ConsoleKey.Q or ConsoleKey.Escape or ConsoleKey.B:
        _state = GameState.Playing;
        break;
    }
  }

  private void TryBuy(int item)
  {
    switch (item)
    {
      case 0 when _score >= 2000 && !_weaponOwned[1]:
        _score -= 2000; _weaponOwned[1] = true; _currentWeapon = 1;
        Console.WriteLine("  Bought Rifle!");
        break;
      case 1 when _score >= 3000 && !_weaponOwned[2]:
        _score -= 3000; _weaponOwned[2] = true; _currentWeapon = 2;
        Console.WriteLine("  Bought Shotgun!");
        break;
      case 2 when _score >= 500 && _health < MaxHealth:
        _score -= 500; _health = MathF.Min(MaxHealth, _health + 50);
        Console.WriteLine("  Bought Health Pack!");
        break;
      case 3 when _score >= 1000:
        _score -= 1000; _cam.MaxSpeed = 20f; _cam.Acceleration = 120f;
        Console.WriteLine("  Bought Speed Boost!");
        break;
      case 4 when _score >= 300:
        _score -= 300;
        foreach (var w in _weapons) w.Ammo = w.MaxAmmo;
        Console.WriteLine("  Bought Ammo Refill!");
        break;
    }
  }

  // ═══════════════════════════════════════════════════════════════════
  // UPDATE
  // ═══════════════════════════════════════════════════════════════════

  private void Update(float dt)
  {
    if (_state != GameState.Playing) return;

    // Weapon
    _weapons[_currentWeapon].Update(dt);
    _hitMarkerTimer = MathF.Max(0, _hitMarkerTimer - dt * 4f);
    _damageFlashTimer = MathF.Max(0, _damageFlashTimer - dt * 3f);

    // ── Continuous input from held keys ──
    float moveF = 0, moveR = 0, lookY = 0, lookP = 0;
    if (_input.IsHeld(ConsoleKey.W)) moveF += 1f;
    if (_input.IsHeld(ConsoleKey.S)) moveF -= 1f;
    if (_input.IsHeld(ConsoleKey.D)) moveR += 1f;
    if (_input.IsHeld(ConsoleKey.A)) moveR -= 1f;
    if (_input.IsHeld(ConsoleKey.J) || _input.IsHeld(ConsoleKey.LeftArrow)) lookY -= 1f;
    if (_input.IsHeld(ConsoleKey.L) || _input.IsHeld(ConsoleKey.RightArrow)) lookY += 1f;
    if (_input.IsHeld(ConsoleKey.I) || _input.IsHeld(ConsoleKey.UpArrow)) lookP += 1f;
    if (_input.IsHeld(ConsoleKey.K) || _input.IsHeld(ConsoleKey.DownArrow)) lookP -= 1f;
    if (_input.ConsumePress(ConsoleKey.Spacebar) && _onGround)
      _velocityY = Physics.JumpVelocity;

    // Camera movement + look (acceleration-based)
    _cam.UpdateMovement(dt, moveF, moveR, lookY, lookP);

    // ── Collision: resolve camera horizontal movement against statics ──
    var statics = _arena.StaticColliders.ToArray().AsSpan();
    {
      AABB playerBox = new(
        new(_cam.Position.X - Camera.PlayerRadius, _cam.Position.Y - Camera.EyeHeight, _cam.Position.Z - Camera.PlayerRadius),
        new(_cam.Position.X + Camera.PlayerRadius, _cam.Position.Y - Camera.EyeHeight + Camera.PlayerHeight, _cam.Position.Z + Camera.PlayerRadius));

      // Check X collision
      foreach (ref readonly var s in statics)
      {
        if (playerBox.Intersects(s))
        {
          // Push back on X
          _cam.Position.X -= _cam.Velocity.X * dt;
          _cam.Velocity.X = 0;
          playerBox = new(
            new(_cam.Position.X - Camera.PlayerRadius, _cam.Position.Y - Camera.EyeHeight, _cam.Position.Z - Camera.PlayerRadius),
            new(_cam.Position.X + Camera.PlayerRadius, _cam.Position.Y - Camera.EyeHeight + Camera.PlayerHeight, _cam.Position.Z + Camera.PlayerRadius));
          break;
        }
      }
      foreach (ref readonly var s in statics)
      {
        if (playerBox.Intersects(s))
        {
          // Push back on Z
          _cam.Position.Z -= _cam.Velocity.Z * dt;
          _cam.Velocity.Z = 0;
          break;
        }
      }
    }

    // Player gravity
    _velocityY -= Physics.Gravity * dt;
    _velocityY = MathF.Max(_velocityY, -Physics.TerminalVelocity);

    Vec3<float> vertVel = new(0, _velocityY, 0);
    AABB vBox = new(
      new(_cam.Position.X - Camera.PlayerRadius, _cam.Position.Y - Camera.EyeHeight, _cam.Position.Z - Camera.PlayerRadius),
      new(_cam.Position.X + Camera.PlayerRadius, _cam.Position.Y - Camera.EyeHeight + Camera.PlayerHeight, _cam.Position.Z + Camera.PlayerRadius));

    Vec3<float> delta = Physics.MoveAndSlide(vBox, vertVel, dt, statics, out _onGround);
    _cam.Position.Y += delta.Y;
    if (_onGround) _velocityY = 0;

    // Clamp to arena
    _cam.Position.X = Math.Clamp(_cam.Position.X, -Arena.HalfSize + 2, Arena.HalfSize - 2);
    _cam.Position.Z = Math.Clamp(_cam.Position.Z, -Arena.HalfSize + 2, Arena.HalfSize - 2);
    if (_cam.Position.Y < Camera.EyeHeight)
    {
      _cam.Position.Y = Camera.EyeHeight;
      _velocityY = 0;
      _onGround = true;
    }

    // Enemies
    foreach (var enemy in _enemies)
    {
      enemy.Update(dt, _cam.Position, statics);

      // Enemy attacks player if close
      if (enemy.State == EnemyState.Active)
      {
        float dist = Vec3Length(enemy.Position - _cam.Position);
        if (dist < 2f)
        {
          _health -= 15f * dt;
          _damageFlashTimer = 0.5f;
          if (_health <= 0)
          {
            _state = GameState.GameOver;
            Console.WriteLine($"=== GAME OVER === Score: {_score}");
          }
        }
      }
    }

    // Remove dead enemies
    _enemies.RemoveAll(e => e.State == EnemyState.Dead);

    // Wave manager
    var spawned = _waveManager.Update(dt);
    _enemies.AddRange(spawned);

    // Check if wave ended and start buy phase
    if (!_waveManager.WaveActive && !_waveManager.BuyPhase && _waveManager.CurrentWave > 0)
    {
      // Auto-start next wave after buy phase
      _score += _waveManager.WaveClearBonus;
      Console.WriteLine($"  Wave {_waveManager.CurrentWave} cleared! +{_waveManager.WaveClearBonus} bonus. Score: {_score}");
    }

    // Auto-start next wave when buy phase ends
    if (_waveManager.CurrentWave > 0 && !_waveManager.WaveActive && !_waveManager.BuyPhase)
    {
      _waveManager.StartNextWave(_arena.EnemySpawnPoints, _rng);
      Console.WriteLine($"=== WAVE {_waveManager.CurrentWave} START ===");
      _state = GameState.Playing;
    }

    // Particles
    for (int i = _particles.Count - 1; i >= 0; i--)
    {
      _particles[i].Life -= dt;
      _particles[i].Position += _particles[i].Velocity * dt;
      _particles[i].Velocity.Y -= 5f * dt; // gravity on particles
      if (_particles[i].Life <= 0) _particles.RemoveAt(i);
    }
  }

  private void TryShoot()
  {
    var weapon = _weapons[_currentWeapon];
    if (!weapon.CanFire)
    {
      if (weapon.Ammo == 0 && !weapon.IsReloading)
        weapon.Reload();
      return;
    }

    weapon.Fire();

    // Muzzle flash particles
    var muzzlePos = _cam.Position + _cam.Forward * 0.5f + new Vec3<float>(0, -0.2f, 0);
    for (int i = 0; i < 3; i++)
    {
      _particles.Add(new Particle
      {
        Position = muzzlePos,
        Velocity = _cam.Forward * 8f + RandomVec3() * 2f,
        Life = 0.15f,
        Size = 0.05f,
        Color = new Vec3<float>(1f, 0.8f, 0.3f),
      });
    }

    // Hitscan
    int pellets = weapon.Pellets;
    for (int p = 0; p < pellets; p++)
    {
      Vec3<float> dir = _cam.Forward;
      if (weapon.Spread > 0)
      {
        dir = dir + RandomVec3() * weapon.Spread;
        float len = Vec3Length(dir);
        dir = dir * (1f / len);
      }

      // Check enemies
      float bestDist = float.MaxValue;
      Enemy? bestEnemy = null;

      foreach (var enemy in _enemies)
      {
        if (enemy.State != EnemyState.Active &&
            !(enemy.Type == EnemyType.Prop && enemy.State == EnemyState.Spawning))
          continue;

        float? dist = Physics.RayIntersect(_cam.Position, dir, enemy.Bounds);
        if (dist.HasValue && dist.Value < bestDist && dist.Value > 0)
        {
          bestDist = dist.Value;
          bestEnemy = enemy;
        }
      }

      if (bestEnemy != null)
      {
        bool killed = bestEnemy.TakeDamage(weapon.Damage);
        _hitMarkerTimer = 1f;

        // Impact particles
        var hitPos = _cam.Position + dir * bestDist;
        for (int i = 0; i < 5; i++)
        {
          _particles.Add(new Particle
          {
            Position = hitPos,
            Velocity = RandomVec3() * 3f,
            Life = 0.4f,
            Size = 0.04f,
            Color = new Vec3<float>(1f, 0.2f, 0.1f),
          });
        }

        if (killed)
        {
          _score += bestEnemy.ScoreValue;
          _waveManager.OnEnemyKilled();
          Console.WriteLine($"  Kill! +{bestEnemy.ScoreValue} ({bestEnemy.Type})");
        }
      }
      else
      {
        // Check arena geometry (wall hit sparks)
        foreach (var col in _arena.StaticColliders)
        {
          float? dist = Physics.RayIntersect(_cam.Position, dir, col);
          if (dist.HasValue && dist.Value > 0 && dist.Value < 100f)
          {
            var hitPos = _cam.Position + dir * dist.Value;
            for (int i = 0; i < 3; i++)
            {
              _particles.Add(new Particle
              {
                Position = hitPos,
                Velocity = RandomVec3() * 2f + new Vec3<float>(0, 2, 0),
                Life = 0.3f,
                Size = 0.03f,
                Color = new Vec3<float>(1f, 0.9f, 0.5f),
              });
            }
            break;
          }
        }
      }
    }
  }

  // ═══════════════════════════════════════════════════════════════════
  // RENDERING HELPERS
  // ═══════════════════════════════════════════════════════════════════

  private static IShader CreateShader(IRenderer renderer, string vert, string frag)
  {
    return renderer.CreateSpirVShader(
    [
      new() { Data = Encoding.UTF8.GetBytes(vert), Type = ShaderProgramType.Vertex },
      new() { Data = Encoding.UTF8.GetBytes(frag), Type = ShaderProgramType.Fragment },
    ]);
  }

  private static (Pipeline pipeline, IBuffer uniformBuffer) CreateSkyPipeline(IRenderer renderer, ITexture tex)
  {
    IShader shader = CreateShader(renderer, Shaders.SkyVert, Shaders.SkyFrag);
    float[] verts = [-1, -1, 3, -1, -1, 3];
    uint[] inds = [0, 1, 2];

    IBuffer vb = renderer.CreateBuffer(verts.Length * sizeof(float));
    IBuffer ib = renderer.CreateBuffer(inds.Length * sizeof(uint));
    IBuffer ub = renderer.CreateBuffer(64);
    vb.Write<float>(verts, 0);
    ib.Write<uint>(inds, 0);

    VertexAttribute[] attribs = [new() { Location = 0, Size = 2, Stride = 2 * sizeof(float), Offset = 0 }];
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

    DrawEntry[] draws = [new(textures: [tex], vertexBuffer: vb, indexBuffer: ib, uniformBuffer: ub,
      vertexOffset: 0, indexStart: 0, indexCount: 3, uniformStart: 0, uniformSize: 64)];

    Pipeline pipeline = new(shader, state, attribs, draws);
    return (pipeline, ub);
  }

  private static (float[] verts, uint[] inds, int bladeCount) BuildGrassMesh(List<Vec3<float>> positions)
  {
    // Each blade: 2 triangles (4 verts, 6 indices)
    // Vertex: pos(3) + uv(2) + grassData(2) = 7 floats
    int count = positions.Count;
    float[] verts = new float[count * 4 * 7];
    uint[] inds = new uint[count * 6];

    for (int i = 0; i < count; i++)
    {
      var pos = positions[i];
      float height = 0.3f + (Noise.Hash((int)(pos.X * 10), (int)(pos.Z * 10), 123) * 0.5f);
      float width = 0.08f + Noise.Hash((int)(pos.X * 7), (int)(pos.Z * 7), 456) * 0.06f;
      float windPhase = Noise.Hash((int)(pos.X * 3), (int)(pos.Z * 3), 789);

      // Random rotation for the blade
      float angle = Noise.Hash((int)(pos.X * 5), (int)(pos.Z * 5), 321) * MathF.PI;
      float cx = MathF.Cos(angle) * width * 0.5f;
      float cz = MathF.Sin(angle) * width * 0.5f;

      int vi = i * 4 * 7;
      int ii = i * 6;
      uint b = (uint)(i * 4);

      // Bottom-left
      verts[vi] = pos.X - cx; verts[vi + 1] = pos.Y; verts[vi + 2] = pos.Z - cz;
      verts[vi + 3] = 0; verts[vi + 4] = 0;
      verts[vi + 5] = windPhase; verts[vi + 6] = 0;

      // Bottom-right
      vi += 7;
      verts[vi] = pos.X + cx; verts[vi + 1] = pos.Y; verts[vi + 2] = pos.Z + cz;
      verts[vi + 3] = 1; verts[vi + 4] = 0;
      verts[vi + 5] = windPhase; verts[vi + 6] = 0;

      // Top-right
      vi += 7;
      verts[vi] = pos.X + cx * 0.3f; verts[vi + 1] = pos.Y + height; verts[vi + 2] = pos.Z + cz * 0.3f;
      verts[vi + 3] = 1; verts[vi + 4] = 1;
      verts[vi + 5] = windPhase; verts[vi + 6] = 1;

      // Top-left
      vi += 7;
      verts[vi] = pos.X - cx * 0.3f; verts[vi + 1] = pos.Y + height; verts[vi + 2] = pos.Z - cz * 0.3f;
      verts[vi + 3] = 0; verts[vi + 4] = 1;
      verts[vi + 5] = windPhase; verts[vi + 6] = 1;

      inds[ii] = b; inds[ii + 1] = b + 1; inds[ii + 2] = b + 2;
      inds[ii + 3] = b + 2; inds[ii + 4] = b + 3; inds[ii + 5] = b;
    }

    return (verts, inds, count);
  }

  private static (float[] verts, uint[] inds, int quadCount) BuildParticleMesh(List<Particle> particles, Camera cam)
  {
    if (particles.Count == 0) return (Array.Empty<float>(), Array.Empty<uint>(), 0);

    int count = Math.Min(particles.Count, 4000);
    float[] verts = new float[count * 4 * 9]; // pos(3) + uv(2) + color(4)
    uint[] inds = new uint[count * 6];

    Vec3<float> right = cam.Right;
    Vec3<float> up = cam.Up;

    for (int i = 0; i < count; i++)
    {
      var p = particles[i];
      float s = p.Size * (p.Life / 0.5f); // shrink as they die
      float alpha = MathF.Min(p.Life * 3f, 1f);

      Vec3<float> r = right * s;
      Vec3<float> u = up * s;

      int vi = i * 4 * 9;
      uint b = (uint)(i * 4);

      void WriteVert(int offset, Vec3<float> pos, float uvx, float uvy)
      {
        int idx = vi + offset * 9;
        verts[idx] = pos.X; verts[idx + 1] = pos.Y; verts[idx + 2] = pos.Z;
        verts[idx + 3] = uvx; verts[idx + 4] = uvy;
        verts[idx + 5] = p.Color.X; verts[idx + 6] = p.Color.Y; verts[idx + 7] = p.Color.Z; verts[idx + 8] = alpha;
      }

      WriteVert(0, p.Position - r - u, 0, 0);
      WriteVert(1, p.Position + r - u, 1, 0);
      WriteVert(2, p.Position + r + u, 1, 1);
      WriteVert(3, p.Position - r + u, 0, 1);

      int ii = i * 6;
      inds[ii] = b; inds[ii + 1] = b + 1; inds[ii + 2] = b + 2;
      inds[ii + 3] = b + 2; inds[ii + 4] = b + 3; inds[ii + 5] = b;
    }

    return (verts, inds, count);
  }

  private (float[] verts, uint[] inds, int quadCount) BuildHUD(float aspect)
  {
    var hud = new HudBuilder();

    if (_state == GameState.MainMenu)
    {
      // Title
      hud.AddQuad(-0.6f, 0.2f, 0.6f, 0.45f, 0.15f, 0.15f, 0.2f, 0.9f);
      // "Play" button
      float playAlpha = _selectedMenuItem == 0 ? 1f : 0.5f;
      hud.AddQuad(-0.25f, -0.05f, 0.25f, 0.1f, 0.2f, 0.8f, 0.2f, playAlpha);
      // "Quit" button
      float quitAlpha = _selectedMenuItem == 1 ? 1f : 0.5f;
      hud.AddQuad(-0.25f, -0.25f, 0.25f, -0.1f, 0.8f, 0.2f, 0.2f, quitAlpha);
      return hud.Build();
    }

    if (_state == GameState.GameOver)
    {
      hud.AddQuad(-0.5f, 0.1f, 0.5f, 0.35f, 0.8f, 0.1f, 0.1f, 0.9f);
      hud.AddQuad(-0.3f, -0.1f, 0.3f, 0.05f, 0.3f, 0.3f, 0.3f, 0.8f);
      return hud.Build();
    }

    if (_state == GameState.BuyMenu)
    {
      // Semi-transparent overlay
      hud.AddQuad(-1f, -1f, 1f, 1f, 0f, 0f, 0f, 0.5f);

      string[] items = ["Rifle [2000]", "Shotgun [3000]", "Health +50 [500]", "Speed [1000]", "Ammo [300]"];
      for (int i = 0; i < items.Length; i++)
      {
        float y = 0.3f - i * 0.15f;
        float alpha = _selectedBuyItem == i ? 1f : 0.4f;
        hud.AddQuad(-0.4f, y - 0.05f, 0.4f, y + 0.05f, 0.2f, 0.5f, 0.2f, alpha);
      }
      return hud.Build();
    }

    // Playing HUD

    // Crosshair
    float crossLen = 0.025f;
    float crossThick = 0.003f;
    hud.AddQuad(-crossLen, -crossThick, crossLen, crossThick, 1, 1, 1, 0.8f);
    hud.AddQuad(-crossThick, -crossLen, crossThick, crossLen, 1, 1, 1, 0.8f);

    // Hit marker
    if (_hitMarkerTimer > 0)
    {
      float hm = 0.03f;
      hud.AddQuad(-hm, -0.001f, hm, 0.001f, 1, 0.2f, 0.2f, _hitMarkerTimer);
      hud.AddQuad(-0.001f, -hm, 0.001f, hm, 1, 0.2f, 0.2f, _hitMarkerTimer);
    }

    // Damage flash (red overlay)
    if (_damageFlashTimer > 0)
      hud.AddQuad(-1, -1, 1, 1, 0.8f, 0, 0, _damageFlashTimer * 0.3f);

    // Health bar (bottom-left)
    float healthPct = _health / MaxHealth;
    hud.AddQuad(-0.95f, -0.95f, -0.55f, -0.9f, 0.2f, 0.2f, 0.2f, 0.6f); // bg
    hud.AddQuad(-0.95f, -0.95f, -0.95f + 0.4f * healthPct, -0.9f,
      healthPct > 0.3f ? 0.2f : 0.8f, healthPct > 0.3f ? 0.8f : 0.2f, 0.2f, 0.8f); // fill

    // Ammo (bottom-right)
    var w = _weapons[_currentWeapon];
    float ammoPct = (float)w.Ammo / w.MaxAmmo;
    hud.AddQuad(0.55f, -0.95f, 0.95f, -0.9f, 0.2f, 0.2f, 0.2f, 0.6f); // bg
    hud.AddQuad(0.55f, -0.95f, 0.55f + 0.4f * ammoPct, -0.9f, 0.9f, 0.7f, 0.2f, 0.8f);
    if (w.IsReloading)
      hud.AddQuad(0.55f, -0.88f, 0.95f, -0.85f, 0.5f, 0.5f, 0.9f, 0.6f);

    // Wave + Score (top)
    hud.AddQuad(-0.2f, 0.88f, 0.2f, 0.95f, 0.1f, 0.1f, 0.15f, 0.6f);

    // Weapon indicator (bottom center)
    float weaponX = -0.1f;
    for (int i = 0; i < 3; i++)
    {
      if (!_weaponOwned[i]) continue;
      float alpha = i == _currentWeapon ? 0.9f : 0.3f;
      hud.AddQuad(weaponX, -0.98f, weaponX + 0.06f, -0.92f, 0.5f, 0.5f, 0.5f, alpha);
      weaponX += 0.08f;
    }

    // Enemies remaining (top-right)
    if (_waveManager.WaveActive)
      hud.AddQuad(0.7f, 0.88f, 0.95f, 0.95f, 0.8f, 0.3f, 0.2f, 0.6f);

    // Buy phase indicator
    if (_waveManager.BuyPhase)
      hud.AddQuad(-0.3f, 0.75f, 0.3f, 0.85f, 0.2f, 0.6f, 0.2f, 0.7f);

    return hud.Build();
  }

  // ═══════════════════════════════════════════════════════════════════
  // HELPERS
  // ═══════════════════════════════════════════════════════════════════

  private void ResetGame()
  {
    _score = 0;
    _health = MaxHealth;
    _state = GameState.MainMenu;
    _enemies.Clear();
    _particles.Clear();
    _waveManager = new WaveManager();
    _cam = new Camera { Position = new(0, Camera.EyeHeight, 20) };
    _velocityY = 0;
    _currentWeapon = 0;
    _weapons[0] = Weapon.CreatePistol();
    _weapons[1] = Weapon.CreateRifle();
    _weapons[2] = Weapon.CreateShotgun();
    _weaponOwned[0] = true;
    _weaponOwned[1] = false;
    _weaponOwned[2] = false;
  }

  private Vec3<float> RandomVec3()
  {
    return new(
      (float)(_rng.NextDouble() * 2 - 1),
      (float)(_rng.NextDouble() * 2 - 1),
      (float)(_rng.NextDouble() * 2 - 1));
  }

  private static float Vec3Length(Vec3<float> v) =>
    MathF.Sqrt(v.X * v.X + v.Y * v.Y + v.Z * v.Z);

  private static void PrintControls()
  {
    Console.WriteLine(
      """
      ═══════════════════════════════════════
        QuantiX: Breach Protocol
      ═══════════════════════════════════════
        WASD        - Move
        Space       - Jump
        IJKL/Arrows - Look around
        F           - Shoot
        R           - Reload
        1/2/3       - Switch weapon
        B           - Buy menu (between waves)
        Enter       - Select
        H           - Show controls
        Q/Esc       - Quit / Back
      ═══════════════════════════════════════
      """);
  }
}

// ═══════════════════════════════════════════════════════════════════════
// Supporting types
// ═══════════════════════════════════════════════════════════════════════

public class Particle
{
  public Vec3<float> Position;
  public Vec3<float> Velocity;
  public Vec3<float> Color;
  public float Life;
  public float Size;
}

/// <summary>
/// Builds screen-space quads for HUD. Vertex: pos(2) + uv(2) + color(4) = 8 floats.
/// </summary>
public class HudBuilder
{
  private readonly List<float> _verts = new();
  private readonly List<uint> _inds = new();
  private int _quadCount;

  public void AddQuad(float x0, float y0, float x1, float y1, float r, float g, float b, float a)
  {
    uint baseIdx = (uint)(_quadCount * 4);

    // BL
    _verts.AddRange([x0, y0, 0, 0, r, g, b, a]);
    // BR
    _verts.AddRange([x1, y0, 1, 0, r, g, b, a]);
    // TR
    _verts.AddRange([x1, y1, 1, 1, r, g, b, a]);
    // TL
    _verts.AddRange([x0, y1, 0, 1, r, g, b, a]);

    _inds.AddRange([baseIdx, baseIdx + 1, baseIdx + 2, baseIdx + 2, baseIdx + 3, baseIdx]);
    _quadCount++;
  }

  public (float[] verts, uint[] inds, int quadCount) Build() =>
    (_verts.ToArray(), _inds.ToArray(), _quadCount);
}
