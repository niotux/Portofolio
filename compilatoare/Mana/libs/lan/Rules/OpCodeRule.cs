namespace LAN.Rules;

public enum InstructionKind
{
  Global,
  Local,
}

public enum InstructionScopeEffect
{
  None,
  ResetsLocalScope,
}

public record OpCodeRule
(
  InstructionKind Kind,
  TemplateRule Template_0,
  TemplateRule Template_1,
  ValueRule Value_0,
  ValueRule Value_1,
  ValueRule Value_2,
  ValueRule Value_3,

  InstructionScopeEffect ScopeEffect,
  byte[]? ListsAfterOp = null
);