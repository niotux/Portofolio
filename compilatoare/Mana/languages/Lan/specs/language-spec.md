# LAN Language Specification

## 1 — Scope

Lan is an intermediate language. It is the lowest layer of the compilation stack.
Other languages (Mana first) compile to it. It compiles to LLVM IR.

It provides the raw material higher languages are built from.

## 2 — Grammar

```
MODULE       := INSTRUCTION*
INSTRUCTION  := OPCODE TYPE_TEMPLATE? TYPE_TEMPLATE? VALUE VALUE VALUE VALUE
VALUE        := IDENTIFIER | LITERAL
IDENTIFIER   := LOCAL | GLOBAL
TYPE_TEMPLATE := `.N` | `.ptr` | `.A+B`
```

## 3 — Core Concepts

A MODULE is a linear sequence of INSTRUCTIONS. There is no nesting or blocks.

An INSTRUCTION is one OPCODE, up to 2 TYPE TEMPLATES, and up to 4 VALUES.
Unused slots are absent. No INSTRUCTION exceeds these bounds.

A VALUE is either an IDENTIFIER or a LITERAL. It represents the actual byte
content of a TYPE TEMPLATE. VALUES carry no semantic meaning — they are raw
memory content.

An IDENTIFIER is a named element of the language. It can name a TYPE TEMPLATE,
a VARIABLE, a FUNCTION, or a CONSTANT. IDENTIFIERS are either LOCAL or GLOBAL.

A TYPE TEMPLATE defines structural shapes — byte sizes and layouts. They are
not types in the semantic sense. They are raw memory dimensions.
`.4+8` ≠ `.12`. The sequence matters. No type inference. No equivalence beyond identity.

## 4 — OPCODE Inventory

### 4.1 — Low-level Operations

Arithmetic, bitwise, and memory OPCODEs that mirror CPU primitives.

| Category   | OPCODEs                                                  |
|------------|----------------------------------------------------------|
| Arithmetic | `add`, `sub`, `mul`, `div`, `mod`, `idiv`, `imod`       |
| Float      | `fadd`, `fsub`, `fmul`, `fdiv`, `fmod`                   |
| Bitwise    | `and`, `or`, `xor`, `shl`, `shr`, `ishr`, `not`         |
| Memory     | `alloc`, `write`, `read`, `gep`                          |
| Conversion | `conv`, `iconv`, `fconv`, `itof`, `utof`, `ftoi`, `ftou` |

### 4.2 — Control Flow

| OPCODEs                                          | Description                 |
|--------------------------------------------------|-----------------------------|
| `label`                                          | Define a jump target        |
| `jmp`                                            | Unconditional jump          |
| `jeq` / `jne`                                    | Equality / inequality       |
| `jlt` / `jgt` / `jle` / `jge`                    | Unsigned comparison         |
| `ijlt` / `ijgt` / `ijle` / `ijge`                | Signed comparison           |
| `fjlt` / `fjgt` / `fjle` / `fjge`               | Float comparison            |

### 4.3 — Functions

| OPCODE           | TYPE TEMPLATES | VALUES   | Description                                                  |
|------------------|----------------|----------|--------------------------------------------------------------|
| `fun`            | `.N`           | `name`   | Declare a FUNCTION. Creates a GLOBAL IDENTIFIER.             |
| `ldarg`          | `.N`           | `name`   | Declare a parameter. Creates a LOCAL IDENTIFIER.             |
| `call`           | —              | `func dest` | Call FUNCTION. Store result in dest.                     |
| `arg`            | —              | `value`  | Pass VALUE to preceding `call`.                            |
| `ret`            | —              | `value`  | Return VALUE from current FUNCTION.                        |

A FUNCTION's body is every INSTRUCTION from its `fun` to the next `fun` (or end of MODULE).
`ldarg` must follow `fun` directly.

### 4.4 — Type Templates (named)

| OPCODE           | TYPE TEMPLATES | VALUES   | Description                                                  |
|------------------|----------------|----------|--------------------------------------------------------------|
| `typedef`        | `.T`           | `name`   | Bind a name to a TYPE TEMPLATE. Creates a GLOBAL IDENTIFIER. |

The name can be used as a TYPE TEMPLATE in other INSTRUCTIONS.

### 4.5 — Global Variables and Constants

`var` and `const` are MODULE-scoped. They differ from `set`, which is local.

| OPCODE           | TYPE TEMPLATES | VALUES          | Description                                              |
|------------------|----------------|-----------------|----------------------------------------------------------|
| `var`            | `.T`           | `name value`    | Declare a global VARIABLE. Creates a GLOBAL IDENTIFIER.   |
| `const`          | `.T`           | `name value`    | Declare a global CONSTANT. Creates a GLOBAL IDENTIFIER.   |

`var` always requires a value. They reside in the data section.

### 4.6 — Macros

OPEN.

### 4.7 — Heap Allocation

An allocator is a pair of FUNCTIONS:

| Slot  | SIGNATURE        | Description    |
|-------|------------------|----------------|
| Alloc | `.ptr Fn(.ptr)`  | size → pointer |
| Free  | `.0 Fn(.ptr)`    | pointer → void |

| OPCODE            | TYPE TEMPLATES | VALUES          | Description                                                    |
|-------------------|----------------|-----------------|----------------------------------------------------------------|
| `sethalloc`       | —              | `alloc free`    | Set active allocator. After `fun`, before all `ldarg`.         |
| `new`             | `.T`           | `dest`          | Allocate T bytes via allocator. Store pointer in dest.         |
| `dispose`         | —              | `dest`          | Free dest via allocator's Free FUNCTION.                       |

`new` and `dispose` are dumb replacements — they call the allocator FUNCTIONS.

### 4.8 — Exports

| OPCODE           | TYPE TEMPLATES | VALUES   | Description                                                  |
|------------------|----------------|----------|--------------------------------------------------------------|
| `export`         | —              | `name`   | Mark an existing GLOBAL IDENTIFIER as public.                |

Works on FUNCTION names, TYPE TEMPLATE names, VARIABLES, and CONSTANTS.

## 5 — Invariants

1. INSTRUCTION shape: 1 OPCODE, 2 TYPE TEMPLATES, 4 VALUES. Fixed.
2. TYPE TEMPLATES are structural — size and layout only. No semantics.
3. Flat INSTRUCTION stream. No nesting. No blocks.
4. MODULE is the top-level unit.

## 6 — Open

| Item | Status |
|------|--------|
| Heap model | DONE — §4.7 |
| Named Types | DONE — §4.4 |
| Global Variables & Constants | DONE — §4.5 |
| Exports | DONE — §4.8 |
| Macros | OPEN — §4.6 |
| [More] | OPEN |
