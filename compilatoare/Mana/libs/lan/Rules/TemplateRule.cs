namespace LAN.Rules;

[Flags]
public enum TemplateStructure
{
  Pointer = 0b1,
  Simple = 0b10,
  Composite = 0b100,
  IEEEFloatingPoint = 0b1000,
  Any = Pointer | Simple | Composite | IEEEFloatingPoint,
}


public record TemplateRule
(
  Appearence Appearence,
  TemplateStructure Structure,
  SymbolConstraint Symbol = SymbolConstraint.Any
)
: ISymbolRule;