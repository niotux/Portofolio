namespace UnivKit.Assets.Common;

public sealed class TextAsset : ReadOnlyTextAsset
{
  public TextAsset(string text)
  : base(text)
  { }

  public new string Text
  {
    get => base.text;
    set => base.text = value;
  }
}

public class ReadOnlyTextAsset
{
  private protected string text;
  public ReadOnlyTextAsset(string text)
  {
    this.text = text;
  }

  public string Text => this.text;
}