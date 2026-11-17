using Oblivion.Engine;
using Oblivion.Engine.Input;
using Oblivion.Engine.Physics;
using Oblivion.Engine.Audio;
using Oblivion.Engine.Graphics;
using FPSGame.Entities;
using FPSGame.Weapons;

namespace FPSGame;

public class Game : GameBase
{
  private Player _player;
  private Weapon _weapon;
  private InputHandler _inputHandler;
  private Camera _camera;

  public Game()
  {
    // Initialize game components
    _inputHandler = new InputHandler();
    _camera = new Camera();
    _weapon = new Weapon();
    _player = new Player(_camera, _weapon);
  }

  protected override void Initialize()
  {
    // Set up the engine
    EngineConfig config = new EngineConfig
    {
      WindowTitle = "Oblivion FPS",
      WindowWidth = 1280,
      WindowHeight = 720,
      IsFullscreen = false,
      TargetFrameRate = 60
    };

    InitializeEngine(config);

    // Load scene
    Scene scene = new Scene();
    scene.LoadLevel("assets/levels/level1.json");

    // Add entities to scene
    scene.AddEntity(_player);
    scene.AddEntity(new Enemy { Position = new Vector3(10, 0, 10) });

    // Set up audio
    AudioSystem.LoadSound("assets/audio/footstep.wav", "footstep");
    AudioSystem.LoadSound("assets/audio/gunshot.wav", "gunshot");
    AudioSystem.LoadMusic("assets/audio/music.mp3", "background");

    // Set up physics
    PhysicsSystem.SetGravity(new Vector3(0, -9.81f, 0));
    PhysicsSystem.SetCollisionLayer("player", 1);
    PhysicsSystem.SetCollisionLayer("enemy", 2);
    PhysicsSystem.SetCollisionLayer("world", 3);

    // Set up rendering
    Renderer.SetSkybox("assets/textures/skybox.png");
    Renderer.SetFog(new FogSettings { Color = new Color(0.5f, 0.5f, 0.6f), Density = 0.01f });

    // Start background music
    AudioSystem.PlayMusic("background", loop: true, volume: 0.5f);
  }

  protected override void Update(float deltaTime)
  {
    // Process input
    _inputHandler.Update(deltaTime);

    // Update player
    _player.Update(deltaTime, _inputHandler);

    // Update weapons
    _weapon.Update(deltaTime, _inputHandler);

    // Update physics
    PhysicsSystem.Update(deltaTime);

    // Update camera
    _camera.Update(deltaTime, _player.Transform.Position, _player.Transform.Rotation);

    // Check for collisions
    PhysicsSystem.CheckCollisions();

    // Check game state
    if (_inputHandler.IsKeyDown(Key.Escape))
    {
      Exit();
    }
  }

  protected override void Render()
  {
    // Clear screen
    Renderer.Clear(new Color(0.1f, 0.1f, 0.15f));

    // Render scene
    Renderer.RenderScene();

    // Render UI
    Renderer.RenderUI();

    // Present frame
    Renderer.Present();
  }

  protected override void Shutdown()
  {
    // Clean up resources
    AudioSystem.StopAllSounds();
    AudioSystem.UnloadAllSounds();
    PhysicsSystem.Cleanup();
  }
}