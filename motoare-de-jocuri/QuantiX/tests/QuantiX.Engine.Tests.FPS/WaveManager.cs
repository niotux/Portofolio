using QuantiX.Engine.Mathematics.Vectors;

namespace QuantiX.Engine.Tests.FPS;

public class WaveManager
{
  public int CurrentWave { get; private set; }
  public bool WaveActive { get; private set; }
  public bool BuyPhase { get; private set; }
  public float BuyTimer { get; private set; }
  public int EnemiesRemaining { get; private set; }

  private const float BuyPhaseDuration = 8f;
  private readonly List<Enemy> _pendingSpawns = new();
  private float _spawnTimer;
  private int _spawnIndex;

  public void StartNextWave(List<Vec3<float>> enemySpawns, Random rng)
  {
    CurrentWave++;
    WaveActive = true;
    BuyPhase = false;
    _pendingSpawns.Clear();
    _spawnIndex = 0;
    _spawnTimer = 0;

    bool isPropHunt = CurrentWave % 5 == 0;

    if (isPropHunt)
    {
      // Prop hunt wave
      int propCount = 3 + CurrentWave / 5 * 2;
      for (int i = 0; i < propCount; i++)
      {
        var pos = enemySpawns[rng.Next(enemySpawns.Count)];
        pos.X += (float)(rng.NextDouble() * 10 - 5);
        pos.Z += (float)(rng.NextDouble() * 10 - 5);
        _pendingSpawns.Add(Enemy.CreateProp(pos));
      }

      // Also add some tanks on later waves
      if (CurrentWave >= 10)
      {
        int tankCount = CurrentWave / 10 * 2;
        for (int i = 0; i < tankCount; i++)
        {
          var pos = enemySpawns[rng.Next(enemySpawns.Count)];
          _pendingSpawns.Add(Enemy.CreateTank(pos));
        }
      }
    }
    else
    {
      int baseCount = 3 + CurrentWave * 2;

      if (CurrentWave <= 4)
      {
        // Runners only
        for (int i = 0; i < baseCount; i++)
        {
          var pos = enemySpawns[rng.Next(enemySpawns.Count)];
          _pendingSpawns.Add(Enemy.CreateRunner(pos));
        }
      }
      else
      {
        // Mix runners + tanks
        int tankCount = CurrentWave / 3;
        int runnerCount = baseCount - tankCount;

        for (int i = 0; i < runnerCount; i++)
        {
          var pos = enemySpawns[rng.Next(enemySpawns.Count)];
          _pendingSpawns.Add(Enemy.CreateRunner(pos));
        }
        for (int i = 0; i < tankCount; i++)
        {
          var pos = enemySpawns[rng.Next(enemySpawns.Count)];
          _pendingSpawns.Add(Enemy.CreateTank(pos));
        }
      }
    }

    // Scale health with wave number
    float healthScale = 1f + (CurrentWave - 1) * 0.1f;
    foreach (var e in _pendingSpawns)
    {
      e.Health *= healthScale;
      e.MaxHealth *= healthScale;
    }

    EnemiesRemaining = _pendingSpawns.Count;
  }

  /// <summary>
  /// Call each frame. Returns newly spawned enemies.
  /// </summary>
  public List<Enemy> Update(float dt)
  {
    var spawned = new List<Enemy>();

    if (BuyPhase)
    {
      BuyTimer -= dt;
      if (BuyTimer <= 0)
        BuyPhase = false;
      return spawned;
    }

    if (!WaveActive) return spawned;

    _spawnTimer -= dt;
    if (_spawnTimer <= 0 && _spawnIndex < _pendingSpawns.Count)
    {
      spawned.Add(_pendingSpawns[_spawnIndex]);
      _spawnIndex++;
      _spawnTimer = 0.5f; // stagger spawns
    }

    return spawned;
  }

  public void OnEnemyKilled()
  {
    EnemiesRemaining--;
    if (EnemiesRemaining <= 0)
    {
      WaveActive = false;
      BuyPhase = true;
      BuyTimer = BuyPhaseDuration;
    }
  }

  public int WaveClearBonus => CurrentWave * 1000;
}
