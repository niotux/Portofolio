using System.Collections.Frozen;
using System.Data;
using System.Reflection.Metadata;
using LAN.Rules;

namespace LAN;

public static class Syntax
{
  private static readonly OpCodeRule[] Rules = new OpCodeRule[256];

  static Syntax()
  {
    Rules[OpCode.Unknown] = OpRuleDef.NonExistent;

    Rules[OpCode.Fun] = OpRuleDef.Fun;
    Rules[OpCode.LdArg] = OpRuleDef.TypedArg;
    Rules[OpCode.Ret] = OpRuleDef.Ret;
    Rules[OpCode.Call] = OpRuleDef.Call;
    Rules[OpCode.Arg] = OpRuleDef.Arg;
    Rules[OpCode.VArg] = OpRuleDef.TypedArg;

    Rules[OpCode.Set] = OpRuleDef.Set;

    Rules[OpCode.Add] = OpRuleDef.BinaryOp;
    Rules[OpCode.Sub] = OpRuleDef.BinaryOp;
    Rules[OpCode.Mul] = OpRuleDef.BinaryOp;
    Rules[OpCode.Div] = OpRuleDef.BinaryOp;
    Rules[OpCode.Mod] = OpRuleDef.BinaryOp;

    Rules[OpCode.IDiv] = OpRuleDef.BinaryOp;
    Rules[OpCode.IMod] = OpRuleDef.BinaryOp;

    Rules[OpCode.FAdd] = OpRuleDef.BinaryOp;
    Rules[OpCode.FSub] = OpRuleDef.BinaryOp;
    Rules[OpCode.FMul] = OpRuleDef.BinaryOp;
    Rules[OpCode.FDiv] = OpRuleDef.BinaryOp;
    Rules[OpCode.FMod] = OpRuleDef.BinaryOp;

    Rules[OpCode.Or] = OpRuleDef.BinaryOp;
    Rules[OpCode.And] = OpRuleDef.BinaryOp;
    Rules[OpCode.Xor] = OpRuleDef.BinaryOp;
    Rules[OpCode.ShL] = OpRuleDef.BinaryOp;
    Rules[OpCode.ShR] = OpRuleDef.BinaryOp;
    Rules[OpCode.IShR] = OpRuleDef.BinaryOp;

    Rules[OpCode.Not] = OpRuleDef.UnaryOp;

    Rules[OpCode.Alloc] = OpRuleDef.Alloc;
    Rules[OpCode.Write] = OpRuleDef.Write;
    Rules[OpCode.Read] = OpRuleDef.Read;
    Rules[OpCode.Gep] = OpRuleDef.Gep;

    Rules[OpCode.Conv] = OpRuleDef.CastWithPointer;
    Rules[OpCode.IConv] = OpRuleDef.Cast;
    Rules[OpCode.FConv] = OpRuleDef.Cast;
    Rules[OpCode.IToF] = OpRuleDef.Cast;
    Rules[OpCode.UToF] = OpRuleDef.Cast;
    Rules[OpCode.FToI] = OpRuleDef.Cast;
    Rules[OpCode.FToU] = OpRuleDef.Cast;

    Rules[OpCode.Label] = OpRuleDef.Label;
    Rules[OpCode.Jmp] = OpRuleDef.Jmp;
    Rules[OpCode.Jeq] = OpRuleDef.JumpCond;
    Rules[OpCode.Jne] = OpRuleDef.JumpCond;
    Rules[OpCode.Jlt] = OpRuleDef.JumpCond;
    Rules[OpCode.Jgt] = OpRuleDef.JumpCond;
    Rules[OpCode.Jle] = OpRuleDef.JumpCond;
    Rules[OpCode.Jge] = OpRuleDef.JumpCond;
    Rules[OpCode.IJlt] = OpRuleDef.JumpCond;
    Rules[OpCode.IJgt] = OpRuleDef.JumpCond;
    Rules[OpCode.IJle] = OpRuleDef.JumpCond;
    Rules[OpCode.IJge] = OpRuleDef.JumpCond;
    Rules[OpCode.FJlt] = OpRuleDef.JumpCond;
    Rules[OpCode.FJgt] = OpRuleDef.JumpCond;
    Rules[OpCode.FJle] = OpRuleDef.JumpCond;
    Rules[OpCode.FJge] = OpRuleDef.JumpCond;

    Rules[OpCode.InlFun] = OpRuleDef.Fun;
    Rules[OpCode.VCall] = OpRuleDef.VCall;
    Rules[OpCode.Type] = OpRuleDef.Type;
    Rules[OpCode.Meta] = OpRuleDef.Meta;
    Rules[OpCode.Export] = OpRuleDef.Export;
    Rules[OpCode.Module] = OpRuleDef.Export;
    Rules[OpCode.LdModule] = OpRuleDef.Meta;

    Rules[OpCode.KeplersThirdLaw] = OpRuleDef.TernaryOp;
  }


  public static OpCodeRule GetRule(byte opCode)
    => Rules[opCode];



  private static class OpRuleDef
  {
    public static OpCodeRule NonExistent { get; } = new
    (
      InstructionKind.Local,
      TempRuleDef.NonExistent,
      TempRuleDef.NonExistent,
      ValRulesDef.NonExistent,
      ValRulesDef.NonExistent,
      ValRulesDef.NonExistent,
      ValRulesDef.NonExistent,
      InstructionScopeEffect.None
    );

    public static OpCodeRule UnaryOp { get; } = new
    (
      InstructionKind.Local,
      TempRuleDef.RequiresSimple,
      TempRuleDef.NonExistent,
      ValRulesDef.Temp0Dest,
      ValRulesDef.Temp0Src,
      ValRulesDef.NonExistent,
      ValRulesDef.NonExistent,
      InstructionScopeEffect.None
    );

    public static OpCodeRule BinaryOp { get; } = new
    (
      InstructionKind.Local,
      TempRuleDef.RequiresSimple,
      TempRuleDef.NonExistent,
      ValRulesDef.Temp0Dest,
      ValRulesDef.Temp0Src,
      ValRulesDef.Temp0Src,
      ValRulesDef.NonExistent,
      InstructionScopeEffect.None
    );

    public static OpCodeRule TernaryOp { get; } = new
    (
      InstructionKind.Local,
      TempRuleDef.RequiresSimple,
      TempRuleDef.NonExistent,
      ValRulesDef.Temp0Dest,
      ValRulesDef.Temp0Src,
      ValRulesDef.Temp0Src,
      ValRulesDef.Temp0Src,
      InstructionScopeEffect.None
    );

    public static OpCodeRule Cast { get; } = new
    (
      InstructionKind.Local,
      TempRuleDef.RequiresSimple,
      TempRuleDef.RequiresSimple,
      ValRulesDef.Temp0Dest,
      ValRulesDef.Temp1Src,
      ValRulesDef.NonExistent,
      ValRulesDef.NonExistent,
      InstructionScopeEffect.None
    );

    public static OpCodeRule CastWithPointer { get; } = new
    (
      InstructionKind.Local,
      TempRuleDef.RequiresSimpleOrPointer,
      TempRuleDef.RequiresSimpleOrPointer,
      ValRulesDef.Temp0Dest,
      ValRulesDef.Temp1Src,
      ValRulesDef.NonExistent,
      ValRulesDef.NonExistent,
      InstructionScopeEffect.None
    );

    public static OpCodeRule Set { get; } = new
    (
      InstructionKind.Local,
      TempRuleDef.RequiresAny,
      TempRuleDef.NonExistent,
      ValRulesDef.Temp0Dest,
      ValRulesDef.Temp0Src,
      ValRulesDef.NonExistent,
      ValRulesDef.NonExistent,
      InstructionScopeEffect.None
    );

    public static OpCodeRule Alloc { get; } = new
    (
      InstructionKind.Local,
      TempRuleDef.RequiresAny,
      TempRuleDef.NonExistent,
      ValRulesDef.PointerResult,
      ValRulesDef.NonExistent,
      ValRulesDef.NonExistent,
      ValRulesDef.NonExistent,
      InstructionScopeEffect.None
    );

    public static OpCodeRule Write { get; } = new
    (
      InstructionKind.Local,
      TempRuleDef.RequiresAny,
      TempRuleDef.NonExistent,
      ValRulesDef.PointerSrc,
      ValRulesDef.Temp0Src,
      ValRulesDef.NonExistent,
      ValRulesDef.NonExistent,
      InstructionScopeEffect.None
    );

    public static OpCodeRule Read { get; } = new
    (
      InstructionKind.Local,
      TempRuleDef.RequiresAny,
      TempRuleDef.NonExistent,
      ValRulesDef.Temp0Dest,
      ValRulesDef.PointerSrc,
      ValRulesDef.NonExistent,
      ValRulesDef.NonExistent,
      InstructionScopeEffect.None
    );

    public static OpCodeRule Gep { get; } = new
    (
      InstructionKind.Local,
      TempRuleDef.RequiresComposite,
      TempRuleDef.NonExistent,
      ValRulesDef.GepDest,
      ValRulesDef.PointerSrc,
      ValRulesDef.PointerSrc,
      ValRulesDef.FieldIdx,
      InstructionScopeEffect.None
    );

    public static OpCodeRule Label { get; } = new
    (
      InstructionKind.Local,
      TempRuleDef.NonExistent,
      TempRuleDef.NonExistent,
      ValRulesDef.Label,
      ValRulesDef.NonExistent,
      ValRulesDef.NonExistent,
      ValRulesDef.NonExistent,
      InstructionScopeEffect.None
    );

    public static OpCodeRule Jmp { get; } = new
    (
      InstructionKind.Local,
      TempRuleDef.NonExistent,
      TempRuleDef.NonExistent,
      ValRulesDef.Label,
      ValRulesDef.NonExistent,
      ValRulesDef.NonExistent,
      ValRulesDef.NonExistent,
      InstructionScopeEffect.None
    );

    public static OpCodeRule JumpCond { get; } = new
    (
      InstructionKind.Local,
      TempRuleDef.RequiresSimple,
      TempRuleDef.NonExistent,
      ValRulesDef.Label,
      ValRulesDef.Temp0Src,
      ValRulesDef.Temp0Src,
      ValRulesDef.NonExistent,
      InstructionScopeEffect.None
    );

    public static OpCodeRule Fun { get; } = new
    (
      InstructionKind.Global,
      TempRuleDef.RequiresAny,
      TempRuleDef.NonExistent,
      ValRulesDef.Func,
      ValRulesDef.NonExistent,
      ValRulesDef.NonExistent,
      ValRulesDef.NonExistent,
      InstructionScopeEffect.ResetsLocalScope
    );

    public static OpCodeRule TypedArg { get; } = new
    (
      InstructionKind.Local,
      TempRuleDef.RequiresAny,
      TempRuleDef.NonExistent,
      ValRulesDef.Temp0Dest,
      ValRulesDef.NonExistent,
      ValRulesDef.NonExistent,
      ValRulesDef.NonExistent,
      InstructionScopeEffect.None,
      [OpCode.Fun, OpCode.InlFun]
    );

    public static OpCodeRule Arg { get; } = new
    (
      InstructionKind.Local,
      TempRuleDef.NonExistent,
      TempRuleDef.NonExistent,
      ValRulesDef.CallArg,
      ValRulesDef.NonExistent,
      ValRulesDef.NonExistent,
      ValRulesDef.NonExistent,
      InstructionScopeEffect.None,
      [OpCode.Call]
    );

    public static OpCodeRule Ret { get; } = new
    (
      InstructionKind.Local,
      TempRuleDef.NonExistent,
      TempRuleDef.NonExistent,
      ValRulesDef.OuterFun,
      ValRulesDef.NonExistent,
      ValRulesDef.NonExistent,
      ValRulesDef.NonExistent,
      InstructionScopeEffect.None
    );

    public static OpCodeRule Call { get; } = new
    (
      InstructionKind.Local,
      TempRuleDef.NonExistent,
      TempRuleDef.NonExistent,
      ValRulesDef.Func,
      ValRulesDef.CallDest,
      ValRulesDef.NonExistent,
      ValRulesDef.NonExistent,
      InstructionScopeEffect.None
    );

    public static OpCodeRule VCall { get; } = new
    (
      InstructionKind.Local,
      TempRuleDef.RequiresAny,
      TempRuleDef.NonExistent,
      ValRulesDef.PointerSrc,
      ValRulesDef.Temp0Dest,
      ValRulesDef.NonExistent,
      ValRulesDef.NonExistent,
      InstructionScopeEffect.None
    );

    public static OpCodeRule Type { get; } = new
    (
      InstructionKind.Global,
      TempRuleDef.RequiresAny,
      TempRuleDef.NonExistent,
      ValRulesDef.TypeName,
      ValRulesDef.NonExistent,
      ValRulesDef.NonExistent,
      ValRulesDef.NonExistent,
      InstructionScopeEffect.None
    );

    public static OpCodeRule Meta { get; } = new
    (
      InstructionKind.Global,
      TempRuleDef.NonExistent,
      TempRuleDef.NonExistent,
      ValRulesDef.AnyValue,
      ValRulesDef.NonExistent,
      ValRulesDef.NonExistent,
      ValRulesDef.NonExistent,
      InstructionScopeEffect.None,
      [OpCode.Fun, OpCode.InlFun, OpCode.Type, OpCode.Module]
    );

    public static OpCodeRule Export { get; } = new
    (
      InstructionKind.Global,
      TempRuleDef.NonExistent,
      TempRuleDef.NonExistent,
      ValRulesDef.ExportIdent,
      ValRulesDef.NonExistent,
      ValRulesDef.NonExistent,
      ValRulesDef.NonExistent,
      InstructionScopeEffect.None
    );
  }


  private static class TempRuleDef
  {
    public static TemplateRule NonExistent { get; } = new
    (
      Appearence.MustNotExist,
      TemplateStructure.Any
    );

    public static TemplateRule RequiresAny { get; } = new
    (
      Appearence.MustExist,
      TemplateStructure.Any
    );

    public static TemplateRule RequiresSimple { get; } = new
    (
      Appearence.MustExist,
      TemplateStructure.Simple
    );

    public static TemplateRule RequiresSimpleOrPointer { get; } = new
    (
      Appearence.MustExist,
      TemplateStructure.Simple | TemplateStructure.Pointer
    );

    public static TemplateRule RequiresComposite { get; } = new
    (
      Appearence.MustExist,
      TemplateStructure.Composite
    );
  }


  private static class ValRulesDef
  {
    public static ValueRule NonExistent { get; } = new
    (
      Appearence.MustNotExist,
      ValueTemplateConstraint.None
    );

    public static ValueRule Temp0Dest { get; } = new
    (
      Appearence.ShouldExist,
      ValueTemplateConstraint.Template_0,
      SymbolConstraint.Writeable
    );

    public static ValueRule Temp0Src { get; } = new
    (
      Appearence.ShouldExist,
      ValueTemplateConstraint.Template_0,
      SymbolConstraint.Readable
    );

    public static ValueRule Temp1Dest { get; } = new
    (
      Appearence.ShouldExist,
      ValueTemplateConstraint.Template_1,
      SymbolConstraint.Writeable
    );

    public static ValueRule Temp1Src { get; } = new
    (
      Appearence.ShouldExist,
      ValueTemplateConstraint.Template_1,
      SymbolConstraint.Readable
    );

    public static ValueRule PointerSrc { get; } = new
    (
      Appearence.ShouldExist,
      ValueTemplateConstraint.Pointer,
      SymbolConstraint.Readable
    );

    public static ValueRule PointerResult { get; } = new
    (
      Appearence.ShouldExist,
      ValueTemplateConstraint.Pointer,
      SymbolConstraint.Writeable
    );

    public static ValueRule FieldIdx { get; } = new
    (
      Appearence.ShouldExist,
      ValueTemplateConstraint.Pointer,
      SymbolConstraint.Constant
    );

    public static ValueRule Label { get; } = new
    (
      Appearence.ShouldExist,
      ValueTemplateConstraint.Label,
      SymbolConstraint.Any
    );

    public static ValueRule Func { get; } = new
    (
      Appearence.ShouldExist,
      ValueTemplateConstraint.Function,
      SymbolConstraint.Readable
    );

    public static ValueRule CallDest { get; } = new
    (
      Appearence.ShouldExist,
      ValueTemplateConstraint.CallReturnMatch,
      SymbolConstraint.Writeable
    );

    public static ValueRule CallArg { get; } = new
    (
      Appearence.ShouldExist,
      ValueTemplateConstraint.CallArgumentMatch,
      SymbolConstraint.Readable
    );

    public static ValueRule OuterFun { get; } = new
    (
      Appearence.ShouldExist,
      ValueTemplateConstraint.OuterFun,
      SymbolConstraint.Readable
    );

    public static ValueRule GepDest { get; } = new
    (
      Appearence.ShouldExist,
      ValueTemplateConstraint.FieldMatch,
      SymbolConstraint.Writeable
    );

    public static ValueRule TypeName { get; } = new
    (
      Appearence.ShouldExist,
      ValueTemplateConstraint.Type,
      SymbolConstraint.Identifier
    );

    public static ValueRule AnyValue { get; } = new
    (
      Appearence.ShouldExist,
      ValueTemplateConstraint.None,
      SymbolConstraint.Any
    );

    public static ValueRule ExportIdent { get; } = new
    (
      Appearence.ShouldExist,
      ValueTemplateConstraint.None,
      SymbolConstraint.Identifier
    );
  }
}