using QuantiX.Engine.Mathematics.Vectors;

namespace QuantiX.Engine.Tests.FPS;

public enum EnemyType { Runner, Tank, Prop }
public enum EnemyState { Spawning, Active, Dying, Dead }

public class Enemy
{
  public Vec3<float> Position;
  public float RotationY;
  public EnemyType Type;
  public EnemyState State;
  public float Health;
  public float MaxHealth;
  public float Speed;
  public float SpawnTimer; // counts down during Spawning
  public float DeathTimer; // counts up during Dying
  public float DamageFlash; // timer for red flash when hit
  public bool IsPropRevealed; // for prop enemies, true once they start moving

  // Cached AABB for collision
  public AABB Bounds => new(
    new(Position.X - 0.4f, Position.Y, Position.Z - 0.4f),
    new(Position.X + 0.4f, Position.Y + 1.8f, Position.Z + 0.4f));

  public static Enemy CreateRunner(Vec3<float> pos)
  {
    return new Enemy
    {
      Position = pos,
      Type = EnemyType.Runner,
      State = EnemyState.Spawning,
      Health = 10,
      MaxHealth = 10,
      Speed = 5f,
      SpawnTimer = 1f,
    };
  }

  public static Enemy CreateTank(Vec3<float> pos)
  {
    return new Enemy
    {
      Position = pos,
      Type = EnemyType.Tank,
      State = EnemyState.Spawning,
      Health = 30,
      MaxHealth = 30,
      Speed = 2.5f,
      SpawnTimer = 1.5f,
    };
  }

  public static Enemy CreateProp(Vec3<float> pos)
  {
    return new Enemy
    {
      Position = pos,
      Type = EnemyType.Prop,
      State = EnemyState.Spawning,
      Health = 15,
      MaxHealth = 15,
      Speed = 6f,
      SpawnTimer = 3f, // longer spawn = stays hidden as prop
      IsPropRevealed = false,
    };
  }

  public void Update(float dt, Vec3<float> playerPos, ReadOnlySpan<AABB> statics)
  {
    DamageFlash = MathF.Max(0, DamageFlash - dt * 4f);

    switch (State)
    {
      case EnemyState.Spawning:
        SpawnTimer -= dt;
        if (SpawnTimer <= 0)
        {
          State = EnemyState.Active;
          if (Type == EnemyType.Prop) IsPropRevealed = true;
        }
        break;

      case EnemyState.Active:
        MoveToward(playerPos, dt, statics);
        break;

      case EnemyState.Dying:
        DeathTimer += dt;
        // Sink into ground
        Position.Y -= dt * 2f;
        if (DeathTimer > 1.5f)
          State = EnemyState.Dead;
        break;
    }
  }

  private void MoveToward(Vec3<float> target, float dt, ReadOnlySpan<AABB> statics)
  {
    Vec3<float> dir = target - Position;
    dir.Y = 0; // stay on ground plane

    float dist = MathF.Sqrt(dir.X * dir.X + dir.Z * dir.Z);
    if (dist < 1.5f) return; // close enough to "attack"

    dir = dir * (1f / dist); // normalize
    RotationY = MathF.Atan2(dir.X, -dir.Z);

    Vec3<float> move = dir * (Speed * dt);

    // Simple collision - try to move, resolve
    AABB mover = Bounds;
    AABB testX = mover.Translated(new Vec3<float>(move.X, 0, 0));
    bool blockedX = false;
    foreach (ref readonly var s in statics)
      if (testX.Intersects(s)) { blockedX = true; break; }

    if (!blockedX) Position.X += move.X;

    AABB testZ = Bounds.Translated(new Vec3<float>(0, 0, move.Z));
    bool blockedZ = false;
    foreach (ref readonly var s in statics)
      if (testZ.Intersects(s)) { blockedZ = true; break; }

    if (!blockedZ) Position.Z += move.Z;
  }

  public bool TakeDamage(float damage)
  {
    if (State != EnemyState.Active && State != EnemyState.Spawning) return false;
    // Props can only be hit once revealed or during spawning
    if (Type == EnemyType.Prop && State == EnemyState.Spawning && !IsPropRevealed)
    {
      // Player shot a prop before it revealed - still counts as hit
      IsPropRevealed = true;
      State = EnemyState.Active;
    }

    Health -= damage;
    DamageFlash = 1f;

    if (Health <= 0)
    {
      State = EnemyState.Dying;
      DeathTimer = 0;
      return true; // killed
    }
    return false;
  }

  public int ScoreValue => Type switch
  {
    EnemyType.Runner => 100,
    EnemyType.Tank => 250,
    EnemyType.Prop => 500,
    _ => 0,
  };
}
