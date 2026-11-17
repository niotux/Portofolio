using OpenUL.Assets;

namespace Game;

public static class Assets
{
  public static T Load<T>(Stream stream)
    where T : IAssetLoader<T>
  {
    return T.Load(stream);
  }

  public static T Save<T>(Stream stream)
    where T : IAssetLoader<T>
  {
    return T.Load(stream);
  }
}