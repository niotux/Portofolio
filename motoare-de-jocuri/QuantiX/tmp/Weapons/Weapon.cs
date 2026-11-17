using Oblivion.Engine.Graphics;
using Oblivion.Engine.Input;

namespace FPSGame.Weapons;

public class Weapon
{
  private Vector3 _position = new Vector3(0.5f, -0.5f, -1.0f);
  private Vector3 _rotation = new Vector3(0, 0, 0);
  private float _fireRate = 0.1f; // seconds between shots
  private float _lastFireTime = 0f;
  private float _reloadTime = 1.5f;
  private bool _isReloading = false;
  private int _ammo = 30;
  private int _maxAmmo = 30;
  private float _recoil = 0;
  private float _recoilRecovery = 0.1f;
  private float _spread = 0.05f;

  public int Ammo => _ammo;
  public bool IsReloading => _isReloading;

  public void Update(float deltaTime, InputHandler input)
  {
    // Handle firing
    if (input.IsMouseButtonDown(MouseButton.Left) && !_isReloading && _ammo > 0)
    {
      _lastFireTime += deltaTime;
      if (_lastFireTime >= _fireRate)
      {
        Fire();
        _lastFireTime = 0f;
      }
    }

    // Handle reloading
    if (input.IsKeyDown(Key.R) && !_isReloading && _ammo < _maxAmmo)
    {
      Reload();
    }

    // Handle recoil recovery
    _recoil = Math.Max(0, _recoil - _recoilRecovery * deltaTime);

    // Handle weapon bobbing
    if (input.IsKeyDown(Key.W) || input.IsKeyDown(Key.A) ||
        input.IsKeyDown(Key.S) || input.IsKeyDown(Key.D))
    {
      float bobSpeed = 8f;
      float bobAmount = 0.05f;
      _position.X += Math.Sin(DateTime.Now.TimeOfDay.TotalMilliseconds * bobSpeed * 0.001f) * bobAmount;
      _position.Y += Math.Abs(Math.Cos(DateTime.Now.TimeOfDay.TotalMilliseconds * bobSpeed * 0.001f)) * bobAmount;
    }
    else
    {
      _position = new Vector3(0.5f, -0.5f, -1.0f);
    }
  }

  private void Fire()
  {
    _ammo--;
    _recoil += 0.02f;

    // Play gunshot sound
    AudioSystem.PlaySound("gunshot", Camera.Transform.Position, volume: 0.5f);

    // Create muzzle flash
    Renderer.CreateMuzzleFlash(Camera.Transform.Position + Camera.Transform.Forward * 2);

    // Raycast for hit detection
    Ray ray = new Ray(Camera.Transform.Position, Camera.Transform.Forward + GetRandomSpread());
    RaycastHit hitInfo;
    if (PhysicsSystem.Raycast(ray, out hitInfo))
    {
      // Handle hit
      if (hitInfo.Entity != null)
      {
        hitInfo.Entity.TakeDamage(25f);
      }

      // Create impact effect
      Renderer.CreateImpactEffect(hitInfo.Point, hitInfo.Normal);
    }
  }

  private Vector3 GetRandomSpread()
  {
    return new Vector3(
        (float)(Random.NextDouble() - 0.5) * _spread,
        (float)(Random.NextDouble() - 0.5) * _spread,
        0
    );
  }

  private void Reload()
  {
    _isReloading = true;
    AudioSystem.PlaySound("reload", Camera.Transform.Position);

    // Simulate reload time
    Task.Delay((int)(_reloadTime * 1000)).ContinueWith(_ =>
    {
      _ammo = _maxAmmo;
      _isReloading = false;
    });
  }

  public void Draw()
  {
    // Draw weapon model
    Matrix worldMatrix = Matrix.CreateTranslation(_position) *
                        Matrix.CreateRotationX(_rotation.X - _recoil) *
                        Matrix.CreateRotationY(_rotation.Y) *
                        Matrix.CreateRotationZ(_rotation.Z);

    Renderer.DrawModel("assets/models/weapon.fbx", new Transform(worldMatrix), "assets/textures/weapon.png");

    // Draw ammo counter
    Renderer.DrawUIText($"Ammo: {_ammo}/{_maxAmmo}", 50, 700, new Color(1, 1, 1), 24);

    // Draw crosshair
    Renderer.DrawUICrosshair(640, 360, new Color(0, 1, 0, 0.8f));
  }
}