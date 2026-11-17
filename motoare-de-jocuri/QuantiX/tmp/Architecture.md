# Oblivion Game Engine Architecture

## High-Level Architecture

```mermaid
graph TB
    subgraph "Game Layer"
        A[Program.cs] --> B[Game.cs]
        B --> C[Player.cs]
        B --> D[Enemy.cs]
        B --> E[Weapon.cs]
        B --> F[InputHandler.cs]
    end

    subgraph "Engine Layer"
        B --> G[Engine Core]
        B --> H[Renderer]
        B --> I[Input System]
        B --> J[Physics System]
        B --> K[Audio System]
        B --> L[SceneManager]
    end

    subgraph "Graphics Layer"
        H --> M[GPU Rendering]
        H --> N[Shader System]
        H --> O[Texture System]
    end

    subgraph "Physics Layer"
        J --> P[Collision Detection]
        J --> Q[Raycasting]
        J --> R[Rigidbody Physics]
    end

    subgraph "Audio Layer"
        K --> S[Sound Effects]
        K --> T[Music Playback]
        K --> U[3D Audio]
    end

    subgraph "Asset Layer"
        C --> V[Models]
        C --> W[Textures]
        D --> X[Animations]
        E --> Y[Audio Files]
    end
```

## Component Interactions

```mermaid
sequenceDiagram
    participant Game
    participant Input
    participant Player
    participant Weapon
    participant Physics
    participant Renderer
    participant Audio

    Game->>Input: Update(deltaTime)
    Input->>Game: Input State
    Game->>Player: Update(deltaTime, input)
    Player->>Player: HandleMouseLook
    Player->>Player: HandleMovement
    Player->>Physics: CheckCollision
    Physics-->>Player: Collision Info
    Player->>Audio: Play Footstep
    Player->>Weapon: Update(deltaTime, input)
    Weapon->>Weapon: Fire (if input)
    Weapon->>Audio: Play Gunshot
    Weapon->>Renderer: Create Muzzle Flash
    Weapon->>Physics: Raycast
    Physics-->>Weapon: Hit Info
    Weapon->>Renderer: Create Impact
    Game->>Physics: Update(deltaTime)
    Game->>Renderer: RenderScene
    Renderer->>Renderer: Draw Models
    Renderer->>Renderer: Draw UI
    Renderer->>Game: Present Frame
```

## Engine API Design

### Core Engine
- `GameBase`: Abstract base class for game implementation
- `EngineConfig`: Configuration for engine initialization
- `Game`: Static access to game instance

### Rendering
- `Renderer`: Main rendering interface
- `Camera`: Camera component with projection/view matrices
- `Transform`: Position, rotation, scale

### Input
- `InputSystem`: Low-level input handling
- `Key` and `MouseButton`: Input enums
- `InputHandler`: Game-specific input abstraction

### Physics
- `PhysicsSystem`: Physics simulation
- `CollisionInfo`: Collision detection results
- `RaycastHit`: Raycast results

### Audio
- `AudioSystem`: Audio management
- Sound effects and music playback
- 3D positional audio

## File Structure

```
Oblivion/
├── Program.cs              # Entry point
├── Game.cs                 # Main game class
├── InputHandler.cs         # Input abstraction
├── Entities/
│   ├── Entity.cs           # Base entity class
│   ├── Player.cs           # Player implementation
│   └── Enemy.cs            # Enemy implementation
├── Weapons/
│   └── Weapon.cs           # Weapon implementation
└── Architecture.md         # This file