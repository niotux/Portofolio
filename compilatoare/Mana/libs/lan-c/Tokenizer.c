#include "Tokenizer.h"
#include "ErrorMessages.h"
#include "Instructions.h"
#include "Keywords.h"
#include "Log.h"
#include "NullChecks.h"

#include <ctype.h>
#include <stdlib.h>
#include <string.h>

// Pointers have no platform-independent byte size, so the type carries a unique
// sentinel instead of a concrete width. Size resolution happens at codegen.
const size_t LANTokenDataBuffer_TypePtr = (size_t)-1;

struct LANTokenArray
{
  size_t Count;
};

static inline LANToken* LANTokenArray_Tokens(LANTokenArray* inst)
{
  return (LANToken*)((char*)inst + sizeof(LANTokenArray));
}

typedef struct
{
  const char* Text;
  LANTokenKind Kind;

} TokenMapEntry;

static const TokenMapEntry Keywords[] = {
  { "ldarg", LANTokenKind_Op_LdArg },
  { "set", LANTokenKind_Op_Set },

  { "add", LANTokenKind_Op_Add },
  { "sub", LANTokenKind_Op_Sub },
  { "mul", LANTokenKind_Op_Mul },
  { "div", LANTokenKind_Op_Div },
  { "mod", LANTokenKind_Op_Mod },

  { "idiv", LANTokenKind_Op_IDiv },
  { "imod", LANTokenKind_Op_IMod },

  { "fadd", LANTokenKind_Op_FAdd },
  { "fsub", LANTokenKind_Op_FSub },
  { "fmul", LANTokenKind_Op_FMul },
  { "fdiv", LANTokenKind_Op_FDiv },
  { "fmod", LANTokenKind_Op_FMod },

  { "or", LANTokenKind_Op_Or },
  { "and", LANTokenKind_Op_And },
  { "xor", LANTokenKind_Op_Xor },
  { "shl", LANTokenKind_Op_ShL },
  { "shr", LANTokenKind_Op_ShR },
  { "ishr", LANTokenKind_Op_IShR },
  { "not", LANTokenKind_Op_Not },

  { "alloc", LANTokenKind_Op_Alloc },
  { "write", LANTokenKind_Op_Write },
  { "read", LANTokenKind_Op_Read },
  { "gep", LANTokenKind_Op_Gep },

  { "conv", LANTokenKind_Op_Conv },
  { "iconv", LANTokenKind_Op_IConv },
  { "fconv", LANTokenKind_Op_FConv },
  { "itof", LANTokenKind_Op_IToF },
  { "utof", LANTokenKind_Op_UToF },
  { "ftoi", LANTokenKind_Op_FToI },
  { "ftou", LANTokenKind_Op_FToU },

  { "label", LANTokenKind_Op_Label },
  { "jmp", LANTokenKind_Op_Jmp },

  { "jeq", LANTokenKind_Op_Jeq },
  { "jne", LANTokenKind_Op_Jne },
  { "jlt", LANTokenKind_Op_Jlt },
  { "jgt", LANTokenKind_Op_Jgt },
  { "jle", LANTokenKind_Op_Jle },
  { "jge", LANTokenKind_Op_Jge },

  { "ijlt", LANTokenKind_Op_IJlt },
  { "ijgt", LANTokenKind_Op_IJgt },
  { "ijle", LANTokenKind_Op_IJle },
  { "ijge", LANTokenKind_Op_IJge },

  { "fjlt", LANTokenKind_Op_FJlt },
  { "fjgt", LANTokenKind_Op_FJgt },
  { "fjle", LANTokenKind_Op_FJle },
  { "fjge", LANTokenKind_Op_FJge },

  { "ret", LANTokenKind_Op_Ret },

  { "fun", LANTokenKind_Op_Fun },
  { "call", LANTokenKind_Op_Call },
  { "arg", LANTokenKind_Op_Arg },

  { NULL, LANTokenKind_Unknown }
};

int LANTokenKind_IsOp(LANTokenKind kind)
{
  return kind > LANTokenKind_Op && kind < LANTokenKind_Val;
}

int LANTokenKind_IsVal(LANTokenKind kind)
{
  return kind > LANTokenKind_Val && kind < LANTokenKind_Temp;
}

int LANTokenKind_IsTemp(LANTokenKind kind)
{
  return kind > LANTokenKind_Temp && kind < LANTokenKind_Symbol;
}

int LANTokenKind_IsSymbol(LANTokenKind kind)
{
  return kind > LANTokenKind_Symbol;
}

uint8_t LANTokenKind_ToInstruction(LANTokenKind kind)
{
  if (LANTokenKind_IsOp(kind))
  {
    return (uint8_t)(kind - LANTokenKind_Op);
  }
  return Op_Unknown;
}

const char* LANTokenKind_Name(LANTokenKind k)
{
  switch (k)
  {
  case LANTokenKind_Unknown:
    return "Unknown";
  case LANTokenKind_Op_LdArg:
    return "Op_LdArg";
  case LANTokenKind_Op_Set:
    return "Op_Set";
  case LANTokenKind_Op_Add:
    return "Op_Add";
  case LANTokenKind_Op_Sub:
    return "Op_Sub";
  case LANTokenKind_Op_Mul:
    return "Op_Mul";
  case LANTokenKind_Op_Div:
    return "Op_Div";
  case LANTokenKind_Op_Mod:
    return "Op_Mod";
  case LANTokenKind_Op_IDiv:
    return "Op_IDiv";
  case LANTokenKind_Op_IMod:
    return "Op_IMod";
  case LANTokenKind_Op_FAdd:
    return "Op_FAdd";
  case LANTokenKind_Op_FSub:
    return "Op_FSub";
  case LANTokenKind_Op_FMul:
    return "Op_FMul";
  case LANTokenKind_Op_FDiv:
    return "Op_FDiv";
  case LANTokenKind_Op_FMod:
    return "Op_FMod";
  case LANTokenKind_Op_Or:
    return "Op_Or";
  case LANTokenKind_Op_And:
    return "Op_And";
  case LANTokenKind_Op_Xor:
    return "Op_Xor";
  case LANTokenKind_Op_ShL:
    return "Op_ShL";
  case LANTokenKind_Op_ShR:
    return "Op_ShR";
  case LANTokenKind_Op_IShR:
    return "Op_IShR";
  case LANTokenKind_Op_Not:
    return "Op_Not";
  case LANTokenKind_Op_Alloc:
    return "Op_Alloc";
  case LANTokenKind_Op_Write:
    return "Op_Write";
  case LANTokenKind_Op_Read:
    return "Op_Read";
  case LANTokenKind_Op_Gep:
    return "Op_Gep";
  case LANTokenKind_Op_Conv:
    return "Op_Conv";
  case LANTokenKind_Op_IConv:
    return "Op_IConv";
  case LANTokenKind_Op_FConv:
    return "Op_FConv";
  case LANTokenKind_Op_IToF:
    return "Op_IToF";
  case LANTokenKind_Op_UToF:
    return "Op_UToF";
  case LANTokenKind_Op_FToI:
    return "Op_FToI";
  case LANTokenKind_Op_FToU:
    return "Op_FToU";
  case LANTokenKind_Op_Label:
    return "Op_Label";
  case LANTokenKind_Op_Jmp:
    return "Op_Jmp";
  case LANTokenKind_Op_Jeq:
    return "Op_Jeq";
  case LANTokenKind_Op_Jne:
    return "Op_Jne";
  case LANTokenKind_Op_Jlt:
    return "Op_Jlt";
  case LANTokenKind_Op_Jgt:
    return "Op_Jgt";
  case LANTokenKind_Op_Jle:
    return "Op_Jle";
  case LANTokenKind_Op_Jge:
    return "Op_Jge";
  case LANTokenKind_Op_IJlt:
    return "Op_IJlt";
  case LANTokenKind_Op_IJgt:
    return "Op_IJgt";
  case LANTokenKind_Op_IJle:
    return "Op_IJle";
  case LANTokenKind_Op_IJge:
    return "Op_IJge";
  case LANTokenKind_Op_FJlt:
    return "Op_FJlt";
  case LANTokenKind_Op_FJgt:
    return "Op_FJgt";
  case LANTokenKind_Op_FJle:
    return "Op_FJle";
  case LANTokenKind_Op_FJge:
    return "Op_FJge";
  case LANTokenKind_Op_Ret:
    return "Op_Ret";
  case LANTokenKind_Op_Fun:
    return "Op_Fun";
  case LANTokenKind_Op_Call:
    return "Op_Call";
  case LANTokenKind_Op_Arg:
    return "Op_Arg";
  case LANTokenKind_Val_Identifier:
    return "Val_Identifier";
  case LANTokenKind_Val_FloatLiteral:
    return "Val_FloatLiteral";
  case LANTokenKind_Val_PosIntLiteral:
    return "Val_PosIntLiteral";
  case LANTokenKind_Val_NegIntLiteral:
    return "Val_NegIntLiteral";
  case LANTokenKind_Val_HexLiteral:
    return "Val_HexLiteral";
  case LANTokenKind_Val_BinLiteral:
    return "Val_BinLiteral";
  case LANTokenKind_Val_OctLiteral:
    return "Val_OctLiteral";
  case LANTokenKind_Val_StringLiteral:
    return "Val_StringLiteral";
  case LANTokenKind_Val_BoolLiteral:
    return "Val_BoolLiteral";
  case LANTokenKind_Temp_Simple:
    return "Temp_Simple";
  case LANTokenKind_Temp_Composed:
    return "Temp_Composed";
  case LANTokenKind_Temp_Ptr:
    return "Temp_Ptr";
  case LANTokenKind_Symbol_InstrEnd:
    return "Symbol_InstrEnd";
  case LANTokenKind_Symbol_BlockBegin:
    return "Symbol_BlockBegin";
  case LANTokenKind_Symbol_BlockEnd:
    return "Symbol_BlockEnd";
  default:
    return "<invalid_token>";
  }
}

// ── Character classification (all from config) ─────────────────────────

// Check if a character is a valid identifier continuation.
// Uses LAN_IDENT_CONTINUATION_CHARS directly.
static int IsIdentifierContinuation(char c)
{
  if (isalnum((unsigned char)c) || c == LAN_UNDERSCORE)
  {
    return 1;
  }
  for (size_t i = 0; i < strlen(LAN_IDENT_CONTINUATION_CHARS); i++)
  {
    if (c == LAN_IDENT_CONTINUATION_CHARS[i])
    {
      return 1;
    }
  }
  return 0;
}

// Check if a character is a hard symbol (always scanned independently).
static int IsHardSymbol(char c)
{
  return c == LAN_SYMBOL_INSTR_END || c == LAN_SYMBOL_BLOCK_BEGIN || c == LAN_SYMBOL_BLOCK_END;
}

// Check if a character is a boundary (skipped as whitespace).
static int IsBoundary(char c)
{
  return isspace((unsigned char)c) || c == LAN_SYMBOL_SEPARATOR;
}

// ── Scanner ────────────────────────────────────────────────────────────
//
// A Span records where in the source buffer a lexeme lives, plus the
// source location (1-based line and column) of its first character.

typedef struct
{
  size_t Start;
  size_t Length;
  size_t Line; // 1-based
  size_t Col;  // 1-based
} Span;

typedef struct
{
  const char* Src;
  size_t Len;
  size_t Pos;
  size_t Line; // 1-based, updated as we advance
  size_t Col;  // 1-based, updated as we advance
} Scanner;

// Advance one character, keeping Line/Col in sync.
static void ScannerAdvance(Scanner* s)
{
  if (s->Pos >= s->Len)
  {
    return;
  }
  if (s->Src[s->Pos] == LAN_NEW_LINE)
  {
    s->Line++;
    s->Col = 1;
  }
  else
  {
    s->Col++;
  }
  s->Pos++;
}

// Advance past the next lexeme and fill *out.
// Returns  1  — a span was produced.
// Returns  0  — end of input.
//
// Templates start with LAN_TEMPLATE_PREFIX (currently '.') and are single
// tokens: .4, .ptr, .ptr+ptr
// The prefix is only treated as a template starter when it begins a token
// (i.e. after whitespace or a hard symbol). Inside identifiers, it is a
// continuation character.

static int NextSpan(Scanner* s, Span* out)
{
  while (s->Pos < s->Len)
  {
    char c = s->Src[s->Pos];

    // Skip whitespace and separators.
    if (IsBoundary(c))
    {
      ScannerAdvance(s);
      continue;
    }

    // Skip single-line comments: // ... \n
    if (c == LAN_COMMENT_CHAR && s->Pos + 1 < s->Len && s->Src[s->Pos + 1] == LAN_COMMENT_CHAR)
    {
      while (s->Pos < s->Len && s->Src[s->Pos] != LAN_NEW_LINE)
      {
        ScannerAdvance(s);
      }
      continue;
    }

    // Record start position before consuming the token.
    size_t tokenLine = s->Line;
    size_t tokenCol = s->Col;

    // Hard single-character symbols: ; { }
    if (IsHardSymbol(c))
    {
      out->Start = s->Pos;
      out->Length = 1;
      out->Line = tokenLine;
      out->Col = tokenCol;
      ScannerAdvance(s);
      return 1;
    }

    // Template token: starts with LAN_TEMPLATE_PREFIX, scans until whitespace,
    // another LAN_TEMPLATE_PREFIX, or end.
    // Examples: .4, .ptr, .ptr+ptr, .4+8
    if (c == LAN_TEMPLATE_PREFIX && s->Pos + 1 < s->Len &&
        (isalnum((unsigned char)s->Src[s->Pos + 1]) ||
         s->Src[s->Pos + 1] == LAN_TEMPLATE_COMPOSE ||
         s->Src[s->Pos + 1] == LAN_UNDERSCORE))
    {
      size_t j = s->Pos + 1;
      while (j < s->Len && !IsBoundary(s->Src[j]) && s->Src[j] != LAN_TEMPLATE_PREFIX)
      {
        j++;
      }
      out->Start = s->Pos;
      out->Length = j - s->Pos;
      out->Line = tokenLine;
      out->Col = tokenCol;
      while (s->Pos < j)
      {
        ScannerAdvance(s);
      }
      return 1;
    }

    // String literal: "..."
    if (c == LAN_STRING_LITERAL)
    {
      size_t j = s->Pos + 1;
      while (j < s->Len && s->Src[j] != LAN_STRING_LITERAL)
      {
        j++;
      }

      int unterminated = j >= s->Len;

      // A terminated string's span includes the closing quote. An
      // unterminated one runs to the end of input and is reported by the
      // classifier as an unterminated literal — never silently dropped.
      size_t end = unterminated ? s->Len : j + 1;
      out->Start = s->Pos;
      out->Length = end - s->Pos;
      out->Line = tokenLine;
      out->Col = tokenCol;
      while (s->Pos < end)
      {
        ScannerAdvance(s);
      }
      return 1;
    }

    // Negative numeric literal: -<digits>
    if (c == LAN_NEGATIVE_MARKER && s->Pos + 1 < s->Len && isdigit((unsigned char)s->Src[s->Pos + 1]))
    {
      size_t j = s->Pos + 1;
      while (j < s->Len && isdigit((unsigned char)s->Src[j]))
      {
        j++;
      }
      out->Start = s->Pos;
      out->Length = j - s->Pos;
      out->Line = tokenLine;
      out->Col = tokenCol;
      while (s->Pos < j)
      {
        ScannerAdvance(s);
      }
      return 1;
    }

    // Positive numeric literal: <digits>
    if (isdigit((unsigned char)c))
    {
      size_t j = s->Pos;
      while (j < s->Len && (isalnum((unsigned char)s->Src[j]) || s->Src[j] == LAN_UNDERSCORE))
      {
        j++;
      }
      out->Start = s->Pos;
      out->Length = j - s->Pos;
      out->Line = tokenLine;
      out->Col = tokenCol;
      while (s->Pos < j)
      {
        ScannerAdvance(s);
      }
      return 1;
    }

    // Identifier or keyword: starts with letter, underscore, or a continuation char
    // Continues with LAN_IDENT_CONTINUATION_CHARS
    if (isalpha((unsigned char)c) || c == LAN_UNDERSCORE || IsIdentifierContinuation(c))
    {
      size_t j = s->Pos;
      while (j < s->Len && IsIdentifierContinuation(s->Src[j]))
      {
        j++;
      }
      out->Start = s->Pos;
      out->Length = j - s->Pos;
      out->Line = tokenLine;
      out->Col = tokenCol;
      while (s->Pos < j)
      {
        ScannerAdvance(s);
      }
      return 1;
    }

    // Unknown character — emit it as a single-character span so the
    // classifier reports it, rather than silently dropping it.
    out->Start = s->Pos;
    out->Length = 1;
    out->Line = tokenLine;
    out->Col = tokenCol;
    ScannerAdvance(s);
    return 1;
  }

  return 0; // end of input
}

// ── Lexeme classification ──────────────────────────────────────────────

typedef struct
{
  const LANTokenKind Kind;
  const LANLogMessage* Error;
  LANTokenArray* Components; // non-NULL only for Temp_Composed
  int SentinelCollision; // true if a component equals LANTokenDataBuffer_TypePtr

} ClassificationResult;

// Parse a simple template component: "N" or "ptr".
// On success writes the component's size to *outSize (the unique pointer
// sentinel LANTokenDataBuffer_TypePtr for "ptr") and the consumed length to
// *consumed, then returns 1. Returns 0 on error. The size is reported through
// an out-param so the pointer sentinel cannot be confused with an error code.
static int ParseTemplateComponent(const char* src, size_t len, size_t* outSize, size_t* consumed)
{
  // Check for the pointer keyword (configurable: "ptr" or "address")
  size_t ptrLen = strlen(LAN_TEMPLATE_PTR_KEYWORD);
  if (len >= ptrLen && memcmp(src, LAN_TEMPLATE_PTR_KEYWORD, ptrLen) == 0)
  {
    *outSize = LANTokenDataBuffer_TypePtr;
    *consumed = ptrLen;
    return 1;
  }

  // Numeric: parse decimal
  unsigned long long val = 0;
  size_t i = 0;
  while (i < len && isdigit((unsigned char)src[i]))
  {
    val = val * 10 + (src[i] - '0');
    i++;
  }
  if (i == 0)
  {
    return 0; // no digits found
  }
  *outSize = (size_t)val;
  *consumed = i;
  return 1;
}

// Count components in a template token (after the prefix).
// Returns -1 on malformed input (missing separator, trailing separator, etc).
static int64_t CountComponents(const char* p, const char* end)
{
  int64_t count = 0;
  while (p < end)
  {
    size_t consumed;
    size_t size;
    if (!ParseTemplateComponent(p, (size_t)(end - p), &size, &consumed))
    {
      return -1;
    }
    count++;
    p += consumed;
    if (p >= end)
    {
      break;
    }
    // Must be a compose separator followed by more input
    if (*p != LAN_TEMPLATE_COMPOSE)
    {
      return -1;
    }
    p++;
    if (p >= end)
    {
      return -1; // trailing separator
    }
  }
  return count;
}

// Classify a template token starting with LAN_TEMPLATE_PREFIX, e.g. ".4", ".ptr+ptr", ".4+8"
static ClassificationResult ClassifyTemplate(LANString* text, size_t* outData)
{
  const char* tokChars = LANString_Chars(text);
  size_t tokLength = LANString_Count(text);

  // Must start with LAN_TEMPLATE_PREFIX
  if (tokLength < 2 || tokChars[0] != LAN_TEMPLATE_PREFIX)
  {
    *outData = 0;
    return (ClassificationResult){
      .Kind = LANTokenKind_Temp_Simple,
      .Error = &LANMsg_Error_Tokenizer_InvalidTemplate,
      .Components = NULL,
      .SentinelCollision = 0,
    };
  }

  // Scan the part after the prefix
  const char* p = tokChars + 1;
  const char* end = tokChars + tokLength;

  int64_t count = CountComponents(p, end);
  if (count < 0)
  {
    *outData = 0;
    return (ClassificationResult){
      .Kind = LANTokenKind_Temp_Simple,
      .Error = &LANMsg_Error_Tokenizer_InvalidTemplate,
      .Components = NULL,
      .SentinelCollision = 0,
    };
  }

  // Build the components sub-array
  size_t compCount = (size_t)count;
  LANTokenArray* comps = LANMakeTokenArray(compCount);
  if (!comps)
  {
    *outData = 0;
    return (ClassificationResult){
      .Kind = LANTokenKind_Temp_Simple,
      .Error = &LANMsg_Error_Tokenizer_InvalidTemplate,
      .Components = NULL,
      .SentinelCollision = 0,
    };
  }

  int hasPtr = 0;
  int numericSentinel = 0; // a numeric component equals the ptr sentinel
  p = tokChars + 1;
  for (size_t i = 0; i < compCount; i++)
  {
    size_t consumed;
    size_t size;
    ParseTemplateComponent(p, (size_t)(end - p), &size, &consumed);

    // A component that starts with a digit and hits the sentinel value is a
    // numeric collision — .18446744073709551615 is indistinguishable from .ptr
    int isNumeric = isdigit((unsigned char)*p);
    int isSentinel = (size == LANTokenDataBuffer_TypePtr);
    if (isNumeric && isSentinel)
    {
      numericSentinel = 1;
    }

    LANString* compText = LANMakeStringOfLength(consumed);
    if (compText == NULL)
    {
      LANFreeTokens(&comps);
      *outData = 0;
      return (ClassificationResult){
        .Kind = LANTokenKind_Temp_Simple,
        .Error = &LANMsg_Error_Tokenizer_InvalidTemplate,
        .Components = NULL,
        .SentinelCollision = 0,
      };
    }
    memcpy(LANString_At(compText, 0), p, consumed);

    int isPtr = isSentinel; // true for both "ptr" keyword and numeric sentinel
    LANToken slot = {
      .Kind = isPtr ? LANTokenKind_Temp_Ptr : LANTokenKind_Temp_Simple,
      .Text = compText,
      .Line = 0,
      .Char = 0,
      .DataBuffer = size,
      .Components = NULL,
    };
    memcpy(LANTokenArray_At(comps, i), &slot, sizeof(LANToken));

    if (isPtr)
    {
      hasPtr = 1;
    }

    p += consumed;
    if (p < end)
    {
      p++; // skip '+'
    }
  }

  if (compCount > 1)
  {
    // Composed: the head carries the components; its own DataBuffer is unused.
    *outData = 0;
    return (ClassificationResult){
      .Kind = LANTokenKind_Temp_Composed,
      .Error = NULL,
      .Components = comps,
      .SentinelCollision = numericSentinel,
    };
  }

  // Single component — the head token carries its size directly; the sub-array
  // is redundant, so free it.
  *outData = LANTokenArray_At(comps, 0)->DataBuffer;
  LANFreeTokens(&comps);
  return (ClassificationResult){
    .Kind = hasPtr ? LANTokenKind_Temp_Ptr : LANTokenKind_Temp_Simple,
    .Error = NULL,
    .Components = NULL,
    .SentinelCollision = numericSentinel,
  };
}

static ClassificationResult ClassifyLexeme(LANString* text, size_t* outData)
{
  size_t tokLength = LANString_Count(text);
  const char* tokChars = LANString_Chars(text);

  *outData = 0;

  if (tokLength == 0)
  {
    return (ClassificationResult){
      .Kind = LANTokenKind_Unknown,
      .Error = NULL,
    };
  }

  // ── Single-character hard symbols ────────────────────────────────────
  if (tokLength == 1)
  {
    switch (tokChars[0])
    {
    case LAN_SYMBOL_INSTR_END:
      return (ClassificationResult){
        .Kind = LANTokenKind_Symbol_InstrEnd,
        .Error = NULL,
      };
    case LAN_SYMBOL_BLOCK_BEGIN:
      return (ClassificationResult){
        .Kind = LANTokenKind_Symbol_BlockBegin,
        .Error = NULL,
      };
    case LAN_SYMBOL_BLOCK_END:
      return (ClassificationResult){
        .Kind = LANTokenKind_Symbol_BlockEnd,
        .Error = NULL,
      };
    default:
      break;
    }
  }

  // ── Template token (starts with LAN_TEMPLATE_PREFIX) ────────────────
  if (tokChars[0] == LAN_TEMPLATE_PREFIX)
  {
    return ClassifyTemplate(text, outData);
  }

  // ── String literal ───────────────────────────────────────────────────
  if (tokChars[0] == LAN_STRING_LITERAL)
  {
    if (tokChars[tokLength - 1] != LAN_STRING_LITERAL || tokLength < 2)
    {
      return (ClassificationResult){
        .Kind = LANTokenKind_Unknown,
        .Error = &LANMsg_Error_Tokenizer_UnterminatedString,
      };
    }
    *outData = tokLength;
    return (ClassificationResult){
      .Kind = LANTokenKind_Val_StringLiteral,
      .Error = NULL,
    };
  }

  // ── Keywords and operators (must come before numeric checks) ─────────
  for (const TokenMapEntry* e = Keywords; e->Text != NULL; e++)
  {
    size_t kn = strlen(e->Text);
    if (kn == tokLength && memcmp(tokChars, e->Text, tokLength) == 0)
    {
      return (ClassificationResult){
        .Kind = e->Kind,
        .Error = NULL,
      };
    }
  }

  // ── Identifier ───────────────────────────────────────────────────────
  // First character must be a letter, underscore, or a non-digit
  // continuation char (e.g. '$'). Digit-leading tokens are numeric
  // literals and fall through to the numeric branches below; the template
  // prefix is already handled above.
  char head = tokChars[0];
  int isLetterStart = isalpha((unsigned char)head) || head == LAN_UNDERSCORE;
  int isSpecialStart = IsIdentifierContinuation(head) && !isdigit((unsigned char)head) && head != LAN_TEMPLATE_PREFIX;

  if (isLetterStart || isSpecialStart)
  {
    return (ClassificationResult){
      .Kind = LANTokenKind_Val_Identifier,
      .Error = NULL,
    };
  }

  // ── Numeric literals ─────────────────────────────────────────────────
  char buf[256];
  if (tokLength >= sizeof(buf))
  {
    return (ClassificationResult){
      .Kind = LANTokenKind_Unknown,
      .Error = &LANMsg_Error_Tokenizer_LiteralTooLong,
    };
  }
  memcpy(buf, tokChars, tokLength);
  buf[tokLength] = '\0';

  char* end = NULL;

  // Hex: 0x…
  if (tokLength >= 2 && buf[0] == LAN_NUMBER_FORMAT_CHAR &&
      (buf[1] == LAN_NUMBER_FORMAT_HEX || buf[1] == LAN_NUMBER_FORMAT_HEX_UPPER))
  {
    if (tokLength < 3)
    {
      return (ClassificationResult){
        .Kind = LANTokenKind_Unknown,
        .Error = &LANMsg_Error_Tokenizer_EmptyHexLiteral,
      };
    }
    unsigned long long val = strtoull(buf, &end, 16);
    if (end != buf + tokLength)
    {
      return (ClassificationResult){
        .Kind = LANTokenKind_Unknown,
        .Error = &LANMsg_Error_Tokenizer_InvalidHexLiteral,
      };
    }
    memcpy(outData, &val, sizeof(val));
    return (ClassificationResult){
      .Kind = LANTokenKind_Val_HexLiteral,
      .Error = NULL,
    };
  }

  // Binary: 0b…
  if (tokLength >= 2 && buf[0] == LAN_NUMBER_FORMAT_CHAR &&
      (buf[1] == LAN_NUMBER_FORMAT_BIN || buf[1] == LAN_NUMBER_FORMAT_BIN_UPPER))
  {
    if (tokLength < 3)
    {
      return (ClassificationResult){
        .Kind = LANTokenKind_Unknown,
        .Error = &LANMsg_Error_Tokenizer_EmptyBinLiteral,
      };
    }
    unsigned long long val = strtoull(buf + 2, &end, 2);
    if (end != buf + tokLength)
    {
      return (ClassificationResult){
        .Kind = LANTokenKind_Unknown,
        .Error = &LANMsg_Error_Tokenizer_InvalidBinLiteral,
      };
    }
    memcpy(outData, &val, sizeof(val));
    return (ClassificationResult){
      .Kind = LANTokenKind_Val_BinLiteral,
      .Error = NULL,
    };
  }

  // Octal: 0o…
  if (tokLength >= 2 && buf[0] == LAN_NUMBER_FORMAT_CHAR &&
      (buf[1] == LAN_NUMBER_FORMAT_OCT || buf[1] == LAN_NUMBER_FORMAT_OCT_UPPER))
  {
    if (tokLength < 3)
    {
      return (ClassificationResult){
        .Kind = LANTokenKind_Unknown,
        .Error = &LANMsg_Error_Tokenizer_EmptyOctLiteral,
      };
    }
    unsigned long long val = strtoull(buf + 2, &end, 8);
    if (end != buf + tokLength)
    {
      return (ClassificationResult){
        .Kind = LANTokenKind_Unknown,
        .Error = &LANMsg_Error_Tokenizer_InvalidOctLiteral,
      };
    }
    memcpy(outData, &val, sizeof(val));
    return (ClassificationResult){
      .Kind = LANTokenKind_Val_OctLiteral,
      .Error = NULL,
    };
  }

  // Negative: starts with LAN_NEGATIVE_MARKER
  if (buf[0] == LAN_NEGATIVE_MARKER)
  {
    if (tokLength < 2 || !isdigit((unsigned char)buf[1]))
    {
      return (ClassificationResult){
        .Kind = LANTokenKind_Unknown,
        .Error = &LANMsg_Error_Tokenizer_StrayMinus,
      };
    }
    long long ival = strtoll(buf, &end, 10);
    if (end != buf + tokLength)
    {
      return (ClassificationResult){
        .Kind = LANTokenKind_Unknown,
        .Error = &LANMsg_Error_Tokenizer_InvalidNegIntLiteral,
      };
    }
    memcpy(outData, &ival, sizeof(ival));
    return (ClassificationResult){
      .Kind = LANTokenKind_Val_NegIntLiteral,
      .Error = NULL,
    };
  }

  // Positive integer: starts with a digit
  if (isdigit((unsigned char)buf[0]))
  {
    unsigned long long ival = strtoull(buf, &end, 10);
    if (end != buf + tokLength)
    {
      return (ClassificationResult){
        .Kind = LANTokenKind_Unknown,
        .Error = &LANMsg_Error_Tokenizer_InvalidPosIntLiteral,
      };
    }
    memcpy(outData, &ival, sizeof(ival));
    return (ClassificationResult){
      .Kind = LANTokenKind_Val_PosIntLiteral,
      .Error = NULL,
    };
  }

  return (ClassificationResult){
    .Kind = LANTokenKind_Unknown,
    .Error = &LANMsg_Error_Tokenizer_InvalidCharacter,
  };
}

// ── LANTokenizeSource ──────────────────────────────────────────────────
//
// Single-pass tokeniser that preserves source location in every token.
//
// Internally it scans twice (once to count, once to fill) just like
// LANSplitTokens, but Span now carries Line/Col so tokens get accurate
// source locations.  The LANString for each token's text is heap-
// allocated and owned by the caller (freed via LANFreeTokens).

LANTokenArray* LANMakeTokenArray(size_t count)
{
  LANTokenArray* result = calloc(1, sizeof(LANTokenArray) + sizeof(LANToken) * count);
  if (result == NULL)
  {
    return NULL;
  }
  result->Count = count;
  return result;
}

LANTokenArray* LANTokenizeSource(LANString* source)
{
  if (source == NULL)
  {
    return NULL;
  }

  Scanner s = {
    .Src = LANString_Chars(source),
    .Len = LANString_Count(source),
    .Pos = 0,
    .Line = 1,
    .Col = 1,
  };

  // First pass — count.
  size_t count = 0;
  Span span;
  while (NextSpan(&s, &span) == 1)
  {
    count++;
  }

  LANTokenArray* result = LANMakeTokenArray(count);
  if (result == NULL)
  {
    return NULL;
  }

  // Second pass — fill.
  s.Pos = 0;
  s.Line = 1;
  s.Col = 1;

  LANToken* slots = LANTokenArray_Tokens(result);

  for (size_t i = 0; i < count; i++)
  {
    NextSpan(&s, &span);

    LANString* lex = LANMakeStringOfLength(span.Length);
    if (lex == NULL)
    {
      // Slots are zero-initialised, so the array owns only the strings
      // filled so far. The destructor frees those and skips the rest.
      LANFreeTokens(&result);
      return NULL;
    }

    memcpy(LANString_At(lex, 0), &s.Src[span.Start], span.Length);

    size_t buffer;
    ClassificationResult classification = ClassifyLexeme(lex, &buffer);
    LANToken value = {
      .Kind = classification.Kind,
      .Text = lex,
      .Line = span.Line,
      .Char = span.Col,
      .DataBuffer = buffer,
      .Components = classification.Components
    };
    memcpy(&slots[i], &value, sizeof(LANToken));
    if (classification.Error != NULL)
    {
      LANLog(classification.Error, &value);
    }
    if (classification.SentinelCollision)
    {
      LANLog(&LANMsg_Warning_Tokenizer_PtrSentinelCollision, &value);
    }
  }

  return result;
}

// ── Public array accessors ─────────────────────────────────────────────

void LANInjectMain(LANTokenArray** instRef)
{
  LANTokenArray* old = *instRef;
  size_t oldCount = old->Count;
  size_t newCount = oldCount + 4;

  LANTokenArray* result = LANMakeTokenArray(newCount);
  if (result == NULL)
  {
    return;
  }

  LANToken prelude[] = {
    { .Kind = LANTokenKind_Op_Fun, .Text = LANMakeString("fun"), .Line = 0, .Char = 0, .DataBuffer = 0, .Components = NULL },
    { .Kind = LANTokenKind_Temp_Simple, .Text = LANMakeString(".4"), .Line = 0, .Char = 0, .DataBuffer = 4, .Components = NULL },
    { .Kind = LANTokenKind_Val_Identifier, .Text = LANMakeString("main"), .Line = 0, .Char = 0, .DataBuffer = 0, .Components = NULL },
    { .Kind = LANTokenKind_Symbol_InstrEnd, .Text = LANMakeString(";"), .Line = 0, .Char = 0, .DataBuffer = 0, .Components = NULL },
  };

  LANToken* slots = LANTokenArray_Tokens(result);
  LANToken* oldSlots = LANTokenArray_Tokens(old);

  // Move the prelude and old tokens into result; their strings now belong
  // to result.
  memcpy(slots, prelude, sizeof(prelude));
  memcpy(slots + 4, oldSlots, sizeof(LANToken) * oldCount);

  // Clear old's slots so disposing it does not free the strings we moved.
  memset(oldSlots, 0, sizeof(LANToken) * oldCount);
  LANFreeTokens(&old);

  *instRef = result;
}

void LANFreeTokens(LANTokenArray** instRef)
{
  if (instRef == NULL || *instRef == NULL)
  {
    return;
  }

  LANToken* slots = LANTokenArray_Tokens(*instRef);
  size_t count = (*instRef)->Count;
  for (size_t i = 0; i < count; i++)
  {
    LANFreeString((LANString**)&slots[i].Text);
    if (slots[i].Components)
    {
      LANFreeTokens(&slots[i].Components);
    }
  }

  free(*instRef);
  *instRef = NULL;
}

int LANToken_EqualsData(LANToken* a, LANToken* b)
{
  if (a == NULL || b == NULL)
  {
    return a == b;
  }

  int aComposed = a->Components != NULL;
  int bComposed = b->Components != NULL;
  if (aComposed != bComposed)
  {
    return 0;
  }

  if (aComposed)
  {
    if (a->Components->Count != b->Components->Count)
    {
      return 0;
    }
    for (size_t i = 0; i < a->Components->Count; i++)
    {
      LANToken* ac = LANTokenArray_At(a->Components, i);
      LANToken* bc = LANTokenArray_At(b->Components, i);
      if (!LANToken_EqualsData(ac, bc))
      {
        return 0;
      }
    }
    return 1;
  }

  return a->DataBuffer == b->DataBuffer;
}

size_t LANTokenArray_Count(LANTokenArray* inst)
{
  return inst->Count;
}

LANToken* LANTokenArray_At(LANTokenArray* inst, size_t index)
{
  if (index >= inst->Count)
  {
    LANLog(&LANMsg_Fatal_Program_OutOfBounds, NULL);
  }
  return &LANTokenArray_Tokens(inst)[index];
}

LANTokenSlice LANTokenArray_Slice(LANTokenArray* inst, size_t start, size_t length)
{
  return (LANTokenSlice){
    .Start = &LANTokenArray_Tokens(inst)[start],
    .Count = length,
  };
}
