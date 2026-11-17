using FPSGame.Entities;

namespace FPSGame;

public static class Game
{
  private static GameInstance _instance;

  public static GameInstance Instance => _instance ?? throw new InvalidOperationException("Game instance not initialized");

  public static void Initialize(GameInstance game)
  {
    _instance = game;
  }

  public static void Shutdown()
  {
    _instance = null;
  }
}

public class GameInstance
{
  public Player Player { get; private set; }

  public void SetPlayer(Player player)
  {
    Player = player;
  }
}