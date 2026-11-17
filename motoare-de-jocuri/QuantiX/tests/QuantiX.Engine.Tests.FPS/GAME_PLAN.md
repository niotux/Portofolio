# QuantiX: Breach Protocol — FPS Stress Test

## Concept

**"Breach Protocol"** — A wave-based tactical arena shooter built on the QuantiX engine.

You're dropped into a procedural training arena (Source engine style: crates, ramps, corridors,
open courtyards). Humanoid target bots spawn in waves and advance toward you. Shoot them
with hitscan before they reach you. Between waves, a buy menu lets you upgrade.

**Creative twist**: Every 5th wave is a "Prop Hunt" wave — some crates/barrels in the arena
are actually disguised enemies that spring to life and rush you. You need sharp eyes.

---

## Architecture

### Game States
```
MainMenu → Playing → BuyMenu → Playing → ... → GameOver → MainMenu
```

### Files
| File | Purpose |
|------|---------|
| `Program.cs` | Entry point, creates GameServer + App |
| `GameServer.cs` | Main game loop, state machine, rendering |
| `Camera.cs` | FPS camera with mouse-look (console keys), position, view/proj |
| `Mat4.cs` | 4x4 matrix math (perspective, lookAt, multiply, translate, rotate, scale) |
| `Physics.cs` | AABB collision, gravity, simple rigid body movement |
| `Arena.cs` | Procedural arena generation — walls, floors, ramps, crates, barrels |
| `MeshBuilder.cs` | Generates vertex/index data for boxes, cylinders, planes, humanoids |
| `Enemy.cs` | Enemy state machine (spawn, advance, attack, die, prop-disguise) |
| `Weapon.cs` | Hitscan weapon logic, fire rate, damage, recoil |
| `WaveManager.cs` | Wave spawning, difficulty curve, prop hunt waves |
| `Particles.cs` | Muzzle flash, impact sparks, blood splats — billboarded quads |
| `GrassField.cs` | GPU grass generation — many billboarded blade quads |
| `TextureGen.cs` | Procedural texture atlas (brick, metal, wood, concrete, camo, grass) |
| `Shaders.cs` | All GLSL shaders centralized |
| `HudRenderer.cs` | Crosshair, health bar, ammo, score, wave counter, kill feed |
| `MenuRenderer.cs` | Main menu and buy menu rendering |
| `Noise.cs` | Perlin/FBM noise (from Rendering test) |

---

## Graphics Pipeline (ordered)

The engine renders pipelines in order. Each frame we build:

### 1. Sky Pipeline
Reuse the procedural sky from the voxel game (day/night cycle, clouds, stars, sun/moon).
Full-screen triangle, DepthWrite=false, DepthFunc=LessOrEqual.

### 2. World Pipeline (Arena geometry)
All static arena geometry in a single large vertex/index buffer.
- **Vertex format**: position(3) + normal(3) + texCoord(2) + ao(1) = 9 floats, stride 36
- **Shader**: Blinn-Phong with directional light + ambient
  - Diffuse: `max(dot(N, L), 0)`
  - Specular: `pow(max(dot(N, H), 0), shininess)` where H = normalize(L + V)
  - Ambient occlusion from vertex AO factor
  - Distance fog blending to sky color
  - Fake projected shadows: darken fragments whose world Y is near ground and are
    "under" something (encoded in AO channel during mesh build)
- **Textures**: Procedural atlas with brick, concrete, metal, wood, crate, barrel textures
- CullMode=Back, DepthWrite=true, DepthFunc=Less

### 3. Physics Objects Pipeline
Dynamic objects (crates, barrels that move when shot).
Same shader as world but with per-object MVP via uniform offset.
Separate pipeline so draw entries can change each frame.

### 4. Enemy Pipeline
Humanoid meshes (box-man: torso, head, arms, legs).
- Same Blinn-Phong shader
- Color tinting via uniform (team color, damage flash)
- Prop-hunt enemies use crate/barrel mesh + texture until they "activate"

### 5. Grass Pipeline
Billboarded grass blades on outdoor ground areas.
- **Vertex format**: position(3) + texCoord(2) + grassData(2) = 7 floats
  - grassData: (windPhase, height)
- **Shader**: Wind animation via sin wave, alpha-tested blade texture
- Thousands of blades for GPU stress
- CullMode=None (double-sided), alpha blending
- DepthWrite=true, DepthFunc=Less

### 6. Particle Pipeline
Billboarded quads for muzzle flash, impact sparks, blood.
- Additive blending for fire/sparks, alpha blending for smoke/blood
- Short-lived, position + velocity + lifetime
- DepthWrite=false, DepthFunc=Less

### 7. HUD Pipeline (screen-space)
- Crosshair (from voxel game, enhanced with hit marker animation)
- Health bar (left), Ammo counter (right), Score (top), Wave counter (top-center)
- Kill feed (top-right, fades out)
- All screen-space quads, DepthFunc=Always, alpha blending

### 8. Menu Pipeline (when in menu state)
- Semi-transparent dark overlay
- Menu items as quads with procedural "text" (block-pixel font)
- Buy menu: grid of upgrade icons

---

## Arena Design

Procedural arena, ~200x200 units:

```
+--------------------------------------------------+
|  [Spawn A]                         [Spawn B]     |
|     ___         ____                   ___        |
|    |   |       |    |                 |   |       |
|    |___|  ___  |____|  ===ramp===     |___|       |
|         |   |                                     |
|   ===   |___|   [courtyard]    ___    ===         |
|                                |   |              |
|    ___    ___________________  |___|   ___        |
|   |   |  |    corridor      |         |   |       |
|   |___|  |__________________|         |___|       |
|                                                   |
|  [Spawn C]     crates  barrels    [Spawn D]       |
+--------------------------------------------------+
```

- **Walls**: Concrete/brick textured boxes
- **Floor**: Tiled concrete with cracks
- **Ramps**: Angled planes connecting elevation changes
- **Crates**: Wooden textured boxes (some are physics objects, some are cover)
- **Barrels**: Cylindrical meshes with metal texture
- **Cover walls**: Half-height walls for tactical gameplay

---

## Physics System

Simple but effective:
- **AABB collision** for player vs world, enemies vs world
- **Gravity**: 9.81 units/s², applied to player (with ground snap)
- **Player**: AABB (0.6 wide, 1.8 tall, 0.6 deep), eye height 1.6
- **Push physics**: Shot crates/barrels get impulse in look direction
- **Enemy movement**: Pathfind via direct line + obstacle avoidance (simple raycast)

---

## Gameplay

### Weapons
- **Pistol** (default): 2 damage, 0.3s fire rate, 12 rounds, 1.5s reload
- **Rifle** (buy): 4 damage, 0.1s fire rate, 30 rounds, 2.0s reload
- **Shotgun** (buy): 8 damage (spread), 0.8s fire rate, 8 rounds, 2.5s reload

### Enemies
- **Runner**: Fast, low HP (10), rushes straight at player
- **Tank**: Slow, high HP (30), takes lots of hits
- **Prop**: Disguised as crate/barrel, 15 HP, surprises player

### Waves
- Wave 1-4: Runners only, increasing count (3, 5, 8, 12)
- Wave 5: **Prop Hunt** (5 props hiding among arena objects)
- Wave 6-9: Mix of Runners + Tanks
- Wave 10: **Prop Hunt** + Tanks
- Pattern repeats with scaling difficulty (more enemies, more HP)

### Scoring
- Runner kill: 100 pts
- Tank kill: 250 pts
- Prop kill: 500 pts (bonus for spotting)
- Shooting a real prop (not enemy): -200 pts
- Wave clear bonus: wave# × 1000

### Buy Menu (between waves)
- Rifle: 2000 pts
- Shotgun: 3000 pts
- Health pack (+50 HP): 500 pts
- Speed boost (30s): 1000 pts
- Ammo refill: 300 pts

---

## Controls

| Key | Action |
|-----|--------|
| W/A/S/D | Move |
| Space | Jump |
| I/J/K/L or Arrows | Look around |
| F | Shoot |
| R | Reload |
| 1/2/3 | Switch weapon |
| B | Open buy menu (between waves) |
| Enter | Select menu item |
| Tab | Show scoreboard |
| H | Show controls |
| Q/Esc | Quit / Back |

---

## Stress Test Targets

- **Geometry**: 50k+ triangles (arena + enemies + grass + particles)
- **Draw calls**: 100+ per frame (individual grass patches, enemies, particles)
- **Shader complexity**: Per-pixel Blinn-Phong, wind animation, fog, AO
- **Texture memory**: 512x512+ procedural atlas
- **Dynamic objects**: 20+ physics objects + 30+ enemies + 200+ particles

---

## Implementation Order

1. **Core** (GameServer, Camera, Mat4, Input handling)
2. **Arena** (MeshBuilder, Arena generation, TextureGen)
3. **Rendering** (Shaders, Sky, World pipeline, HUD)
4. **Grass** (GrassField generation + wind shader)
5. **Physics** (AABB collision, gravity, push)
6. **Enemies** (Spawning, movement, death)
7. **Weapons** (Hitscan, fire rate, ammo, reload)
8. **Waves** (WaveManager, difficulty scaling, prop hunt)
9. **Menus** (Main menu, buy menu, game over)
10. **Polish** (Particles, kill feed, hit markers, score)
