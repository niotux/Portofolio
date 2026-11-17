using Oblivion.Engine.Graphics;
using Oblivion.Engine.Physics;

namespace FPSGame.Entities;

public class Enemy : Entity
{
  private float _health = 100f;
  private float _speed = 2.5f;
  private float _damage = 10f;
  private float _lastAttackTime = 0f;
  private float _attackCooldown = 1.5f;
  private bool _isDead = false;

  public float Health => _health;
  public bool IsDead => _isDead;

  public override void Update(float deltaTime, InputHandler input)
  {
    if (_isDead) return;

    // Simple AI: move towards player
    Transform.LookAt(Game.Instance.Player.Transform.Position);

    Vector3 direction = Transform.Forward;
    Vector3 moveVector = direction * _speed * deltaTime;

    // Check for collision before moving
    Vector3 newPosition = Transform.Position + moveVector;
    if (!PhysicsSystem.CheckCollision(newPosition, Transform.Scale))
    {
      Transform.Position = newPosition;
    }

    // Attack player if close enough
    float distanceToPlayer = Vector3.Distance(Transform.Position, Game.Instance.Player.Transform.Position);
    if (distanceToPlayer < 3.0f)
    {
      _lastAttackTime += deltaTime;
      if (_lastAttackTime >= _attackCooldown)
      {
        Game.Instance.Player.TakeDamage(_damage);
        _lastAttackTime = 0f;
      }
    }
  }

  public void TakeDamage(float amount)
  {
    _health -= amount;
    if (_health <= 0)
    {
      _health = 0;
      _isDead = true;
      // Play death animation or sound
      AudioSystem.PlaySound("enemy_death", Transform.Position);
    }
  }

  public override void Draw()
  {
    if (_isDead) return;

    // Draw enemy model
    Renderer.DrawModel("assets/models/enemy.fbx", Transform, "assets/textures/enemy.png");

    // Draw health bar
    Vector3 screenPos = Camera.WorldToScreen(Transform.Position + new Vector3(0, 2, 0));
    float healthPercent = _health / 100f;

    Renderer.DrawUIRect(screenPos.X - 20, screenPos.Y - 30, 40, 5, new Color(1, 0, 0));
    Renderer.DrawUIRect(screenPos.X - 20, screenPos.Y - 30, 40 * healthPercent, 5, new Color(0, 1, 0));
  }
}