using Oblivion.Engine.Graphics;
using Oblivion.Engine.Physics;
using FPSGame.Weapons;

namespace FPSGame.Entities;

public class Player : Entity
{
  private readonly Camera _camera;
  private readonly Weapon _weapon;
  private float _moveSpeed = 5.0f;
  private float _sprintSpeed = 8.0f;
  private float _mouseSensitivity = 0.002f;
  private bool _isGrounded = false;
  private float _jumpForce = 10.0f;
  private float _verticalRotation = 0;

  public Player(Camera camera, Weapon weapon)
  {
    _camera = camera;
    _weapon = weapon;
    Transform.Position = new Vector3(0, 2, 0);
  }

  public override void Update(float deltaTime, InputHandler input)
  {
    // Handle mouse look
    HandleMouseLook(input);

    // Handle movement
    HandleMovement(input, deltaTime);

    // Handle jumping
    HandleJump(input);

    // Update weapon
    _weapon.Update(deltaTime, input);
  }

  private void HandleMouseLook(InputHandler input)
  {
    float mouseX = input.GetAxis("Mouse X");
    float mouseY = input.GetAxis("Mouse Y");

    // Yaw (horizontal rotation)
    Transform.Rotation.Y += mouseX * _mouseSensitivity;

    // Pitch (vertical rotation) with clamping
    _verticalRotation -= mouseY * _mouseSensitivity;
    _verticalRotation = Math.Clamp(_verticalRotation, -89.0f, 89.0f);
    _camera.Transform.Rotation.X = _verticalRotation;
  }

  private void HandleMovement(InputHandler input, float deltaTime)
  {
    Vector3 direction = Vector3.Zero;

    if (input.IsKeyDown(Key.W))
      direction += Transform.Forward;
    if (input.IsKeyDown(Key.S))
      direction -= Transform.Forward;
    if (input.IsKeyDown(Key.A))
      direction -= Transform.Right;
    if (input.IsKeyDown(Key.D))
      direction += Transform.Right;

    direction = Vector3.Normalize(direction);

    float speed = input.IsKeyDown(Key.LeftShift) ? _sprintSpeed : _moveSpeed;

    Vector3 moveVector = direction * speed * deltaTime;

    // Apply movement with collision detection
    Vector3 newPosition = Transform.Position + moveVector;

    if (PhysicsSystem.CheckCollision(newPosition, Transform.Scale))
    {
      // Simple slide along walls
      Vector3 slideDirection = Vector3.Normalize(moveVector);
      slideDirection.Y = 0; // Don't slide up/down walls

      if (!PhysicsSystem.CheckCollision(Transform.Position + slideDirection * moveVector.Length, Transform.Scale))
      {
        Transform.Position += slideDirection * moveVector.Length;
      }
    }
    else
    {
      Transform.Position = newPosition;
    }

    // Footstep audio
    if (input.IsKeyDown(Key.W) || input.IsKeyDown(Key.A) ||
        input.IsKeyDown(Key.S) || input.IsKeyDown(Key.D))
    {
      if (_isGrounded && !AudioSystem.IsPlaying("footstep"))
      {
        AudioSystem.PlaySound("footstep", Transform.Position, loop: false, volume: 0.3f);
      }
    }
  }

  private void HandleJump(InputHandler input)
  {
    if (input.IsKeyDown(Key.Space) && _isGrounded)
    {
      Velocity.Y = _jumpForce;
      _isGrounded = false;
    }
  }

  public override void OnCollision(CollisionInfo collision)
  {
    if (collision.Normal.Y > 0.5f)
    {
      _isGrounded = true;
      Velocity.Y = 0;
    }
  }

  public override void Draw()
  {
    // Player is invisible (first person)
    // Only the weapon is visible
    _weapon.Draw();
  }
}