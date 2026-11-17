namespace QuantiX.Engine.Tests.FPS;

public enum WeaponType { Pistol, Rifle, Shotgun }

public class Weapon
{
  public WeaponType Type;
  public float Damage;
  public float FireRate; // seconds between shots
  public int MaxAmmo;
  public int Ammo;
  public float ReloadTime;
  public int Pellets; // shotgun has multiple
  public float Spread; // radians, for shotgun

  public float FireCooldown;
  public float ReloadCooldown;
  public bool IsReloading;

  public static Weapon CreatePistol() => new()
  {
    Type = WeaponType.Pistol,
    Damage = 5,
    FireRate = 0.3f,
    MaxAmmo = 12,
    Ammo = 12,
    ReloadTime = 1.5f,
    Pellets = 1,
    Spread = 0,
  };

  public static Weapon CreateRifle() => new()
  {
    Type = WeaponType.Rifle,
    Damage = 4,
    FireRate = 0.1f,
    MaxAmmo = 30,
    Ammo = 30,
    ReloadTime = 2.0f,
    Pellets = 1,
    Spread = 0.01f,
  };

  public static Weapon CreateShotgun() => new()
  {
    Type = WeaponType.Shotgun,
    Damage = 6,
    FireRate = 0.8f,
    MaxAmmo = 8,
    Ammo = 8,
    ReloadTime = 2.5f,
    Pellets = 6,
    Spread = 0.08f,
  };

  public void Update(float dt)
  {
    FireCooldown = MathF.Max(0, FireCooldown - dt);

    if (IsReloading)
    {
      ReloadCooldown -= dt;
      if (ReloadCooldown <= 0)
      {
        Ammo = MaxAmmo;
        IsReloading = false;
      }
    }
  }

  public bool CanFire => !IsReloading && FireCooldown <= 0 && Ammo > 0;

  public void Fire()
  {
    if (!CanFire) return;
    Ammo--;
    FireCooldown = FireRate;
  }

  public void Reload()
  {
    if (IsReloading || Ammo == MaxAmmo) return;
    IsReloading = true;
    ReloadCooldown = ReloadTime;
  }
}
