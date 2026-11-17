using Oblivion.Engine.Graphics;
using Oblivion.Engine.Physics;

namespace FPSGame.Entities;

public abstract class Entity : ICollidable
{
  private Transform _transform = new Transform();
  private Vector3 _velocity = Vector3.Zero;
  private float _mass = 1.0f;

  public Transform Transform => _transform;
  public Vector3 Velocity => _velocity;
  public float Mass => _mass;

  public abstract void Update(float deltaTime, InputHandler input);
  public abstract void Draw();

  public virtual void TakeDamage(float amount)
  {
    // Default implementation does nothing
  }

  public virtual void OnCollision(CollisionInfo collision)
  {
    // Default implementation does nothing
  }

  public void ApplyForce(Vector3 force)
  {
    _velocity += force / _mass;
  }

  public void ApplyImpulse(Vector3 impulse)
  {
    _velocity += impulse / _mass;
  }
}