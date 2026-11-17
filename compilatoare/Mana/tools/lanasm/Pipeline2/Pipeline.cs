namespace LAN.Assembler.Pipeline2;

public delegate TOut Transformer<TIn, TOut>(TIn input)
  where TIn : allows ref struct
  where TOut : allows ref struct;

public static class Pipeline
{
  extension<TIn, TOut>(TIn)
  {
    public static TOut operator |(TIn input, Transformer<TIn, TOut> transformer)
      => transformer(input);
  }
}