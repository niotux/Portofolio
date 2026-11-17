namespace LAN.Rules;

public interface ISymbolRule
{
  Appearence Appearence { get; }
  SymbolConstraint Symbol { get; }
}