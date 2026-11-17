# Lan Grammar

## Language Structure

Lan is a flat list of instructions. Code is structured with `fun` declarations, but the instruction stream itself remains linear — no nested blocks or scopes in the instruction model.

Top-level instructions (before the first `fun`) belong to an implicit global function with `.0` return size.

## Instruction Format

```
<op> [.<template> [+<template>]] [<arg0> <arg1> <arg2> <arg3>];
```

- `op` — instruction name (see instructions.md)
- `template` — type size in bytes (e.g., `.4`, `.8`, `.ptr`)
  - Composite types: `.A+B` adds sizes (`.ptr+ptr` = two pointer-sized slots)
  - Most ops have 0 or 1 template; conversion ops have 2
- `arg0`–`arg3` — operands (0–4 depending on op)
- `;` — instruction terminator

Preferred notation uses a space between op and template: `op .N` rather than `op.N`.

## Examples

```
set .4 x 42;            // op=set, template=.4, arg0=x, arg1=42
add .4 result a b;      // op=add, template=.4, args: result, a, b
conv .4 .8 dest src;    // op=conv, templates=.4, .8
jmp loop;               // op=jmp, no template, arg0=loop
ret x;                   // op=ret, no template, arg0=x
```

## Templates (Type Sizes)

Templates specify **size in bytes**:

| Syntax | Meaning |
|--------|---------|
| `.N`   | N bytes |
| `.ptr` | Native pointer size (4 or 8 bytes depending on architecture) |
| `.A+B` | Composite: sum of sizes (e.g. `.ptr+ptr` = 2×pointer, `.4+8` = 12) |

## Values

- **Identifiers**: `x`, `result`, `loop_label`, `Program.Main`, `expr..0`
  - May contain: `[a-zA-Z0-9_.]$`
  - `$`, `.`, and digits are valid continuation characters
- **Integers**: `42`, `-5`, `0xFF`, `0b1010`, `0o77`
- **Booleans**: `true`, `false`

## Identifiers

Identifiers can contain letters, digits, `_`, `.`, and `$`. This allows names like:

```
Program.Main
expr..0
Base.IO.WriteLn$ofString$$ofEncoding$val2
```

There are no reserved identifiers — context disambiguates ops from names.

## Labels

```
label loop;
```

A label marks a position for jump targets.

## Comments

```
// single line comment
```

## Complete Program

```
fun .4 Program.Add;
  ldarg .4 left;
  ldarg .4 right;
  add .4 res left right;
ret res;

fun .0 Program.Main;
  ldarg .ptr args;
  call Program.Add expr..0;
    arg 10;
    arg 20;
  set .ptr+ptr GlobalStuff 0x0;
ret 0;
```

## Grammar Rules Summary

```
program       := instr*
instr         := IDENT ('.' IDENT)+ ('+' IDENT)* value* ';'
label         := LABEL_KW IDENT ';'
value         := IDENT | LITERAL
template      := N | ptr | template+template
comment       := '//' TEXT
```
