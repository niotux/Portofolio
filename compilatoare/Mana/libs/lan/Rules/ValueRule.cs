namespace LAN.Rules;

public enum ValueTemplateConstraint
{
  None,

  Template_0,
  Template_1,

  Value_0,
  Value_1,
  Value_2,
  Value_3,

  CallArgumentMatch,
  CallReturnMatch,
  OuterFun,
  Function,
  Type,
  FieldMatch,
  Pointer,
  Label,
}

public record ValueRule
(
  Appearence Appearence,
  ValueTemplateConstraint TypeConstraint,
  SymbolConstraint Symbol = SymbolConstraint.Any
)
: ISymbolRule;