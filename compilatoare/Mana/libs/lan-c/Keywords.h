#pragma once
#include <stddef.h>

// ── Language Syntax Configuration ───────────────────────────────────────
//
// Every syntactic choice the tokenizer makes lives here.
// Change it in ONE place, not scattered across the codebase.

// Identifier rules
//   First character:   [a-zA-Z_]
//   Continuation chars: digits, '_', '.', '$'
//   Change to '@' instead of '$'? Just edit this line.
#define LAN_IDENT_CONTINUATION_CHARS ".$_"

// Template prefix character (the '.' in `.4`, `.ptr`)
// Change to ':' (`:4`)? Edit this line.
#define LAN_TEMPLATE_PREFIX '.'

// Template composition separator ('+' in `.ptr+ptr`)
#define LAN_TEMPLATE_COMPOSE '+'

// Template keyword for pointer size
// Change "ptr" to "address"? Edit this line.
#define LAN_TEMPLATE_PTR_KEYWORD "ptr"

// Hard symbols (single-character, always scanned independently)
#define LAN_SYMBOL_INSTR_END   ';'
#define LAN_SYMBOL_BLOCK_BEGIN '{'
#define LAN_SYMBOL_BLOCK_END   '}'

// Boundary characters (skipped as whitespace)
#define LAN_SYMBOL_SEPARATOR   ','
#define LAN_NEW_LINE           '\n'

// Comment prefix
#define LAN_COMMENT_CHAR       '/'

// String literal delimiter
#define LAN_STRING_LITERAL     '"'

// Numeric literal prefixes
#define LAN_NUMBER_FORMAT_CHAR '0'
#define LAN_NUMBER_FORMAT_HEX  'x'
#define LAN_NUMBER_FORMAT_HEX_UPPER 'X'
#define LAN_NUMBER_FORMAT_BIN  'b'
#define LAN_NUMBER_FORMAT_BIN_UPPER 'B'
#define LAN_NUMBER_FORMAT_OCT  'o'
#define LAN_NUMBER_FORMAT_OCT_UPPER 'O'

// Negative literal marker
#define LAN_NEGATIVE_MARKER    '-'

// Underscore (shared)
#define LAN_UNDERSCORE         '_'
