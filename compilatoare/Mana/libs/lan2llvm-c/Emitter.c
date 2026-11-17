#include "Emitter.h"

#include "Context.h"

#include "lan.h"
#include <stdarg.h>
#include <stdbool.h>
#include <stdio.h>
#include <stdlib.h>

static void Write(LAN2LLVMContext* ctx, const char* fmtText, ...)
{
  va_list args;
  va_start(args, fmtText);

  if (!LAN2LLVMContext_HasOutput(ctx))
  {
    size_t length = (size_t)vsnprintf(NULL, 0, fmtText, args);

    va_end(args);
    LAN2LLVMContext_MoveCursor(ctx, length);
    return;
  }

  char* chars = LANString_Chars(LAN2LLVMContext_Output(ctx)) + LAN2LLVMContext_Cursor(ctx);
  size_t remaining = LANString_Count(LAN2LLVMContext_Output(ctx)) - LAN2LLVMContext_Cursor(ctx) + 1;

  size_t length = (size_t)vsnprintf(chars, remaining, fmtText, args);

  va_end(args);
  LAN2LLVMContext_MoveCursor(ctx, length);
  return;
}

static void WriteType(LAN2LLVMContext* ctx, size_t size, LAN2LLVMType type)
{
  if (size == 0)
  {
    Write(ctx, "void");
    return;
  }
  if (type == LAN2LLVMFloat)
  {
    if (size <= 2)
    {
      Write(ctx, "half");
    }
    else if (size <= 4)
    {
      Write(ctx, "float");
    }
    else if (size <= 8)
    {
      Write(ctx, "double");
    }
    return;
  }
  if (type == LAN2LLVMPtr)
  {
    Write(ctx, "ptr");
  }

  Write(ctx, "i%zu", size * 8);
}

//
// Helpers
//

#define IdFloat "%%%s.f.%zu"
#define IdInt "%%%s.i.%zu"
#define IdLiteral "%s"

#define FloatParam IdFloat ", "
#define IntParam IdInt ", "
#define LiteralParam "%s, "

#define End "\n"

typedef struct
{
  const size_t Variant;
  const char* Text;
  const LAN2LLVMType Type;
  const bool IsLiteral;

} ValName;

static ValName GetValName(LAN2LLVMContext* ctx, LANToken* token, LAN2LLVMType type)
{
  if (token->Kind != Val_Identifier)
  {
    return (ValName){
      .IsLiteral = true,
      .Text = LANString_Chars(token->Text),
      .Type = LAN2LLVMInt,
      .Variant = 0,
    };
  }

  LAN2LLVMIdentifier* id = LAN2LLVMContext_GetId(ctx, token);
  if (id == NULL)
  {
    id = LAN2LLVMContext_RegisterId(ctx, token, type);
    return (ValName){
      .IsLiteral = false,
      .Text = LANString_Chars(token->Text),
      .Type = type,
      .Variant = 0,
    };
  }

  id = LAN2LLVMContext_GetId(ctx, token);
  return (ValName){
    .IsLiteral = false,
    .Text = LANString_Chars(token->Text),
    .Type = id->LastType,
    .Variant = id->LastType == LAN2LLVMInt ? id->IntVariants - 1 : id->FloatVariants - 1,
  };
}

static ValName GetNewValName(LAN2LLVMContext* ctx, LANToken* token, LAN2LLVMType type)
{
  LAN2LLVMIdentifier* id = LAN2LLVMContext_RegisterId(ctx, token, type);
  return (ValName){
    .IsLiteral = false,
    .Text = LANString_Chars(token->Text),
    .Type = type,
    .Variant = type == LAN2LLVMInt ? id->IntVariants - 1 : id->FloatVariants - 1,
  };
}

//
// Emitters
//

static void WriteValue(LAN2LLVMContext* ctx, ValName* name)
{
  if (name->IsLiteral)
  {
    Write(ctx, IdLiteral, name->Text);
    return;
  }
  if (name->Type == LAN2LLVMInt)
  {
    Write(ctx, IdInt, name->Text, name->Variant);
    return;
  }
  if (name->Type == LAN2LLVMFloat)
  {
    Write(ctx, IdFloat, name->Text, name->Variant);
    return;
  }
}

static void WriteValueParam(LAN2LLVMContext* ctx, ValName* name)
{
  if (name->IsLiteral)
  {
    Write(ctx, LiteralParam, name->Text);
    return;
  }
  if (name->Type == LAN2LLVMInt)
  {
    Write(ctx, IntParam, name->Text, name->Variant);
    return;
  }
  if (name->Type == LAN2LLVMFloat)
  {
    Write(ctx, FloatParam, name->Text, name->Variant);
    return;
  }
}

static ValName TryWrite_Cast_GetValName(LAN2LLVMContext* ctx, LANToken* token, LAN2LLVMType type, size_t typeSize)
{
  ValName name = GetValName(ctx, token, type);
  if (name.Type != type)
  {
    ValName dest = GetNewValName(ctx, token, type);

    Write(ctx, "  ");
    WriteValue(ctx, &dest);
    Write(ctx, " = bitcast ");
    WriteType(ctx, typeSize, name.Type);
    Write(ctx, " ");
    WriteValue(ctx, &name);
    Write(ctx, " to ");
    WriteType(ctx, typeSize, type);
    Write(ctx, End);
    return dest;
  }
  return name;
}

static void Emit_BinOperation(LAN2LLVMContext* ctx, LANAstModule* module, LANAstInstr* instr, const char* textArg, LAN2LLVMType type1, LAN2LLVMType type2)
{
  ValName arg1 = TryWrite_Cast_GetValName(ctx, instr->ValueArg1, type1, instr->TypeArg0->DataBuffer);
  ValName arg2 = TryWrite_Cast_GetValName(ctx, instr->ValueArg2, type1, instr->TypeArg0->DataBuffer);

  ValName dest = GetNewValName(ctx, instr->ValueArg0, type1);

  Write(ctx, "  ");
  WriteValue(ctx, &dest);
  Write(ctx, " = %s ", textArg);
  WriteType(ctx, instr->TypeArg0->DataBuffer, type1);
  Write(ctx, " ");
  WriteValueParam(ctx, &arg1);
  WriteValue(ctx, &arg2);
  Write(ctx, End);
}
static void Emit_Not(LAN2LLVMContext* ctx, LANAstModule* module, LANAstInstr* instr, const char* textArg, LAN2LLVMType type1, LAN2LLVMType type2)
{
  ValName src = GetValName(ctx, instr->ValueArg1, type1);
  ValName dest = GetNewValName(ctx, instr->ValueArg0, type1);

  Write(ctx, "  ");
  WriteValue(ctx, &dest);
  Write(ctx, " = %s ", textArg);
  WriteType(ctx, instr->TypeArg0->DataBuffer, type1);
  Write(ctx, " ");
  WriteValueParam(ctx, &src);
  Write(ctx, "-1");
  Write(ctx, End);
}

static void Emit_Set(LAN2LLVMContext* ctx, LANAstModule* module, LANAstInstr* instr, const char* textArg, LAN2LLVMType type1, LAN2LLVMType type2)
{
  ValName src = GetValName(ctx, instr->ValueArg1, type1);
  ValName dest = GetNewValName(ctx, instr->ValueArg0, type1);

  Write(ctx, "  ");
  WriteValue(ctx, &dest);
  Write(ctx, " = %s ", textArg);
  WriteType(ctx, instr->TypeArg0->DataBuffer, type1);
  Write(ctx, " ");
  WriteValueParam(ctx, &src);
  Write(ctx, "0");
  Write(ctx, End);
}

static void Emit_LdArg(LAN2LLVMContext* ctx, LANAstModule* module, LANAstInstr* instr, const char* textArg, LAN2LLVMType type1, LAN2LLVMType type2)
{
  ValName dest = GetNewValName(ctx, instr->ValueArg0, type1);
  WriteType(ctx, instr->TypeArg0->DataBuffer, type1);
  Write(ctx, " ");
  WriteValue(ctx, &dest);
}

static void Emit_Ret(LAN2LLVMContext* ctx, LANAstModule* module, LANAstInstr* instr, const char* textArg, LAN2LLVMType type1, LAN2LLVMType type2)
{
  size_t size = LAN2LLVMContext_GetOuterFunc(ctx)->TypeArg0->DataBuffer;
  ValName val = TryWrite_Cast_GetValName(ctx, instr->ValueArg0, type1, size);

  Write(ctx, "  ");
  Write(ctx, "%s ", textArg);
  WriteType(ctx, size, type1);
  Write(ctx, " ");
  WriteValue(ctx, &val);
  Write(ctx, End);
}

//
// Conversions
//

static void Emit_Conversion(LAN2LLVMContext* ctx, LANAstModule* module, LANAstInstr* instr, const char* textArg, LAN2LLVMType type1, LAN2LLVMType type2)
{
  size_t srcSize = instr->TypeArg1->DataBuffer;
  size_t dstSize = instr->TypeArg0->DataBuffer;

  int sameDomain = type1 == type2;
  if (sameDomain && srcSize == dstSize)
  {
    return;
  }

  const char* opcode = textArg;
  if (sameDomain && srcSize > dstSize)
  {
    opcode = type1 == LAN2LLVMInt ? "trunc" : "fptrunc";
  }

  ValName src = GetValName(ctx, instr->ValueArg1, type1);
  ValName dest = GetNewValName(ctx, instr->ValueArg0, type2);

  Write(ctx, "  ");
  WriteValue(ctx, &dest);
  Write(ctx, " = %s ", opcode);
  WriteType(ctx, srcSize, type1);
  Write(ctx, " ");
  WriteValue(ctx, &src);
  Write(ctx, " to ");
  WriteType(ctx, dstSize, type2);
  Write(ctx, End);
}

static void Emit_Fun(LAN2LLVMContext* ctx, LANAstModule* module, LANAstInstr* instr, const char* textArg, LAN2LLVMType type1, LAN2LLVMType type2)
{
  LAN2LLVMContext_SetOuterFunc(ctx, instr);
  LAN2LLVMContext_ClearIdList(ctx);
  Write(ctx, "define ");
  WriteType(ctx, instr->TypeArg0->DataBuffer, type1);
  Write(ctx, " @%s(", LANString_Chars(instr->ValueArg0->Text));
}

static void Emit_Call(LAN2LLVMContext* ctx, LANAstModule* module, LANAstInstr* instr, const char* textArg, LAN2LLVMType type1, LAN2LLVMType type2)
{
  LANAstInstr* funcDecl = LANAstModule_GetFunc(module, instr->ValueArg0);
  LAN2LLVMContext_SetLastCalledFunc(ctx, funcDecl);
  ValName dest = GetNewValName(ctx, instr->ValueArg1, LAN2LLVMInt);
  Write(ctx, "  ");
  WriteValue(ctx, &dest);
  Write(ctx, " = call ");
  WriteType(ctx, funcDecl->TypeArg0->DataBuffer, type1);
  Write(ctx, " @%s(", LANString_Chars(instr->ValueArg0->Text));
}

static void Emit_Arg(LAN2LLVMContext* ctx, LANAstModule* module, LANAstInstr* instr, const char* textArg, LAN2LLVMType type1, LAN2LLVMType type2)
{
  LANAstInstr* funcDecl = LAN2LLVMContext_GetLastCalledFunc(ctx);
  ValName dest = GetValName(ctx, instr->ValueArg0, LAN2LLVMInt);
  WriteType(ctx, (funcDecl + instr->Op->DataBuffer + 1)->TypeArg0->DataBuffer, type1);
  Write(ctx, " ");
  WriteValue(ctx, &dest);
}

typedef void (*EmitFunc)(LAN2LLVMContext* ctx, LANAstModule* module, LANAstInstr* instr, const char* textArg, LAN2LLVMType type1, LAN2LLVMType type2);

typedef struct
{
  const EmitFunc Callback;
  const char* TextArg;
  const LAN2LLVMType Type1;
  const LAN2LLVMType Type2;

} Emitter;

static Emitter Emitters[] = {
  [Op_Call] = { .Callback = Emit_Call, .TextArg = "", .Type1 = LAN2LLVMInt, .Type2 = LAN2LLVMInt },
  [Op_Fun] = { .Callback = Emit_Fun, .TextArg = "", .Type1 = LAN2LLVMInt, .Type2 = LAN2LLVMInt },
  [Op_Arg] = { .Callback = Emit_Arg, .TextArg = "", .Type1 = LAN2LLVMInt, .Type2 = LAN2LLVMInt },
  [Op_LdArg] = { .Callback = Emit_LdArg, .TextArg = "add", .Type1 = LAN2LLVMInt, .Type2 = LAN2LLVMInt },
  [Op_Set] = { .Callback = Emit_Set, .TextArg = "add", .Type1 = LAN2LLVMInt, .Type2 = LAN2LLVMInt },
  [Op_Add] = { .Callback = Emit_BinOperation, .TextArg = "add", .Type1 = LAN2LLVMInt, .Type2 = LAN2LLVMInt },
  [Op_Sub] = { .Callback = Emit_BinOperation, .TextArg = "sub", .Type1 = LAN2LLVMInt, .Type2 = LAN2LLVMInt },
  [LANTokenKind_Op_Mul] = { .Callback = Emit_BinOperation, .TextArg = "mul", .Type1 = LAN2LLVMInt, .Type2 = LAN2LLVMInt },
  [Op_Div] = { .Callback = Emit_BinOperation, .TextArg = "udiv", .Type1 = LAN2LLVMInt, .Type2 = LAN2LLVMInt },
  [Op_Mod] = { .Callback = Emit_BinOperation, .TextArg = "urem", .Type1 = LAN2LLVMInt, .Type2 = LAN2LLVMInt },
  [Op_IDiv] = { .Callback = Emit_BinOperation, .TextArg = "sdiv", .Type1 = LAN2LLVMInt, .Type2 = LAN2LLVMInt },
  [Op_IMod] = { .Callback = Emit_BinOperation, .TextArg = "srem", .Type1 = LAN2LLVMInt, .Type2 = LAN2LLVMInt },
  [Op_FAdd] = { .Callback = Emit_BinOperation, .TextArg = "fadd", .Type1 = LAN2LLVMFloat, .Type2 = LAN2LLVMFloat },
  [Op_FSub] = { .Callback = Emit_BinOperation, .TextArg = "fsub", .Type1 = LAN2LLVMFloat, .Type2 = LAN2LLVMFloat },
  [Op_FMul] = { .Callback = Emit_BinOperation, .TextArg = "fmul", .Type1 = LAN2LLVMFloat, .Type2 = LAN2LLVMFloat },
  [Op_FDiv] = { .Callback = Emit_BinOperation, .TextArg = "fdiv", .Type1 = LAN2LLVMFloat, .Type2 = LAN2LLVMFloat },
  [Op_FMod] = { .Callback = Emit_BinOperation, .TextArg = "frem", .Type1 = LAN2LLVMFloat, .Type2 = LAN2LLVMFloat },
  [Op_And] = { .Callback = Emit_BinOperation, .TextArg = "and", .Type1 = LAN2LLVMInt, .Type2 = LAN2LLVMInt },
  [Op_Or] = { .Callback = Emit_BinOperation, .TextArg = "or", .Type1 = LAN2LLVMInt, .Type2 = LAN2LLVMInt },
  [Op_Xor] = { .Callback = Emit_BinOperation, .TextArg = "xor", .Type1 = LAN2LLVMInt, .Type2 = LAN2LLVMInt },
  [Op_ShL] = { .Callback = Emit_BinOperation, .TextArg = "shl", .Type1 = LAN2LLVMInt, .Type2 = LAN2LLVMInt },
  [Op_ShR] = { .Callback = Emit_BinOperation, .TextArg = "lshr", .Type1 = LAN2LLVMInt, .Type2 = LAN2LLVMInt },
  [Op_IShR] = { .Callback = Emit_BinOperation, .TextArg = "ashr", .Type1 = LAN2LLVMInt, .Type2 = LAN2LLVMInt },
  [Op_Not] = { .Callback = Emit_Not, .TextArg = "xor", .Type1 = LAN2LLVMInt, .Type2 = LAN2LLVMInt },
  [Op_Alloc] = { .Callback = NULL, .TextArg = "", .Type1 = LAN2LLVMInt, .Type2 = LAN2LLVMInt },
  [Op_Write] = { .Callback = NULL, .TextArg = "", .Type1 = LAN2LLVMInt, .Type2 = LAN2LLVMInt },
  [Op_Read] = { .Callback = NULL, .TextArg = "", .Type1 = LAN2LLVMInt, .Type2 = LAN2LLVMInt },
  [Op_Gep] = { .Callback = NULL, .TextArg = "", .Type1 = LAN2LLVMInt, .Type2 = LAN2LLVMInt },
  [Op_Conv] = { .Callback = Emit_Conversion, .TextArg = "zext", .Type1 = LAN2LLVMInt, .Type2 = LAN2LLVMInt },
  [Op_IConv] = { .Callback = Emit_Conversion, .TextArg = "sext", .Type1 = LAN2LLVMInt, .Type2 = LAN2LLVMInt },
  [Op_FConv] = { .Callback = Emit_Conversion, .TextArg = "fpext", .Type1 = LAN2LLVMFloat, .Type2 = LAN2LLVMFloat },
  [Op_IToF] = { .Callback = Emit_Conversion, .TextArg = "sitofp", .Type1 = LAN2LLVMInt, .Type2 = LAN2LLVMFloat },
  [Op_UToF] = { .Callback = Emit_Conversion, .TextArg = "uitofp", .Type1 = LAN2LLVMInt, .Type2 = LAN2LLVMFloat },
  [Op_FToI] = { .Callback = Emit_Conversion, .TextArg = "fptosi", .Type1 = LAN2LLVMFloat, .Type2 = LAN2LLVMInt },
  [Op_FToU] = { .Callback = Emit_Conversion, .TextArg = "fptoui", .Type1 = LAN2LLVMFloat, .Type2 = LAN2LLVMInt },
  [Op_Label] = { .Callback = NULL, .TextArg = "", .Type1 = LAN2LLVMInt, .Type2 = LAN2LLVMInt },
  [Op_Jmp] = { .Callback = NULL, .TextArg = "", .Type1 = LAN2LLVMInt, .Type2 = LAN2LLVMInt },
  [Op_Jeq] = { .Callback = NULL, .TextArg = "", .Type1 = LAN2LLVMInt, .Type2 = LAN2LLVMInt },
  [Op_Jne] = { .Callback = NULL, .TextArg = "", .Type1 = LAN2LLVMInt, .Type2 = LAN2LLVMInt },
  [Op_Jlt] = { .Callback = NULL, .TextArg = "", .Type1 = LAN2LLVMInt, .Type2 = LAN2LLVMInt },
  [Op_Jgt] = { .Callback = NULL, .TextArg = "", .Type1 = LAN2LLVMInt, .Type2 = LAN2LLVMInt },
  [Op_Jle] = { .Callback = NULL, .TextArg = "", .Type1 = LAN2LLVMInt, .Type2 = LAN2LLVMInt },
  [Op_Jge] = { .Callback = NULL, .TextArg = "", .Type1 = LAN2LLVMInt, .Type2 = LAN2LLVMInt },
  [Op_IJlt] = { .Callback = NULL, .TextArg = "", .Type1 = LAN2LLVMInt, .Type2 = LAN2LLVMInt },
  [Op_IJgt] = { .Callback = NULL, .TextArg = "", .Type1 = LAN2LLVMInt, .Type2 = LAN2LLVMInt },
  [Op_IJle] = { .Callback = NULL, .TextArg = "", .Type1 = LAN2LLVMInt, .Type2 = LAN2LLVMInt },
  [Op_IJge] = { .Callback = NULL, .TextArg = "", .Type1 = LAN2LLVMInt, .Type2 = LAN2LLVMInt },
  [Op_FJlt] = { .Callback = NULL, .TextArg = "", .Type1 = LAN2LLVMInt, .Type2 = LAN2LLVMInt },
  [Op_FJgt] = { .Callback = NULL, .TextArg = "", .Type1 = LAN2LLVMInt, .Type2 = LAN2LLVMInt },
  [Op_FJle] = { .Callback = NULL, .TextArg = "", .Type1 = LAN2LLVMInt, .Type2 = LAN2LLVMInt },
  [Op_FJge] = { .Callback = NULL, .TextArg = "", .Type1 = LAN2LLVMInt, .Type2 = LAN2LLVMInt },
  [Op_Ret] = { .Callback = Emit_Ret, .TextArg = "ret", .Type1 = LAN2LLVMInt, .Type2 = LAN2LLVMInt },
};

void LAN2LLVMEmmitInstruction(LAN2LLVMContext* context, LANAstModule* module, LANAstInstr* instr)
{
  LANTokenKind key = instr->Op->Kind;

  const char* textArg = Emitters[key].TextArg;
  const LAN2LLVMType type1 = Emitters[key].Type1;
  const LAN2LLVMType type2 = Emitters[key].Type2;
  if (Emitters[key].Callback != NULL)
  {
    Emitters[key].Callback(context, module, instr, textArg, type1, type2);
  }
  else
  {
    Write(context, "// < ??? >\n");
  }
}

void LAN2LLVMEmmitModule(LAN2LLVMContext* context, LANAstModule* module)
{
  LANAstInstr* lastInstr = NULL;
  for (size_t i = 0; i < LANAstModule_InstrCount(module); i++)
  {
    LANAstInstr* nextInstr = LANAstModule_InstrAt(module, i);
    if (lastInstr != NULL)
    {
      LANTokenKind lastKind = lastInstr->Op->Kind;
      LANTokenKind nextKind = nextInstr->Op->Kind;
      int betArgs = lastKind == Op_Arg && nextKind == Op_Arg;
      int betLdArgs = lastKind == LANTokenKind_Op_LdArg && nextKind == LANTokenKind_Op_LdArg;
      int afterFun = lastKind == Op_Fun || lastKind == LANTokenKind_Op_LdArg;
      int afterCall = lastKind == Op_Call || lastKind == Op_Arg;
      int nextNotLdArgs = nextKind != LANTokenKind_Op_LdArg;
      int nextNotArgs = nextKind != Op_Arg;
      if (betArgs || betLdArgs)
      {
        Write(context, ", ");
      }
      else if (afterFun && nextNotLdArgs)
      {
        Write(context, ")\n{\n");
      }
      else if (afterCall && nextNotArgs)
      {
        Write(context, ")\n");
      }

      if (nextKind == Op_Fun)
      {
        Write(context, "}\n");
      }
    }
    LAN2LLVMEmmitInstruction(context, module, nextInstr);
    lastInstr = nextInstr;
  }
  if (lastInstr == NULL)
  {
    return;
  }
  if (lastInstr->Op->Kind == Op_Fun)
  {
    Write(context, ") { \n");
  }
  Write(context, "}");
}