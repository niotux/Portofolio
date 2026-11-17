# AST → Bytecode Plan

## Statuses
- `InDiscussion` — still open for debate
- `Approved` — agreed upon, ready to implement
- `Implemented` — done

---

## Section 0: Artifact Model

**Status: Approved**

Bytecode is a **flat byte array**. It is not a parsed struct and exposes no
fields — only the raw buffer and a cursor over it.

- **Produced** from an AST module.
- **Loaded** from an existing byte buffer.
- **Read** by a streaming decoder that walks one instruction record at a time,
  in place, decoding on demand. Nothing is materialised into a tree.

The container is agnostic to what the bytes *mean*. It stores and returns
component data faithfully; interpreting any value is the caller's concern, not
the format's.

---

## Section 1: Container Layout

### 1.1 — Four contiguous regions
**Status: Approved**

```
+--------+---------------+--------------+----------------+
| Header | Constant Pool | Instructions | Symbol Table |
+--------+---------------+--------------+----------------+
```

No gaps, no padding between regions. Each region's byte size lives in the header,
so the decoder seeks to any region without walking the one before it.

### 1.2 — Header (56 bytes, fixed)
**Status: InDiscussion** *(grown from 24 B to carry section sizes + the function table)*

| Offset | Size | Field                              |
|-------:|-----:|------------------------------------|
|      0 |    4 | Magic `0x4D414E41` ("MANA")        |
|      4 |    2 | Version (major, minor)             |
|      6 |    2 | Reserved (0)                       |
|      8 |    8 | Constant pool — entry count (u64)  |
|     16 |    8 | Constant pool — byte size (u64)    |
|     24 |    8 | Instructions — record count (u64)  |
|     32 |    8 | Instructions — byte size (u64)     |
|     40 |    8 | Function table — entry count (u64) |
|     48 |    8 | Function table — byte size (u64)   |

Total **56 bytes**. All multi-byte fields little-endian; every `u64` is
8-aligned. No architecture field — the format is platform-neutral. Sizes are
stored (not just offsets) so a future section is just one more count/size pair,
without invalidating any existing field.

---

## Section 2: Constant Pool

### 2.1 — One region holds every constant, deduplicated
**Status: Approved**

Identifier names, labels, function names, string literals, numbers, and type
components all live here as **raw byte buffers**. The pool draws no type
distinction — meaning comes from the instruction that references the entry.

Identical entries — matched on their **full encoded bytes** (tag, size, and
data) — are stored once. Two entries with the same data but different tags stay
distinct.

**Index 0 is reserved as the null sentinel. Real entries start at index 1.**

### 2.2 — Entry format
**Status: InDiscussion**

```
+----------+----------------+---------------+
| Tag (1B) | Size (per tag) | Data (Size B) |
+----------+----------------+---------------+
```

| Tag | Size field | Max data | Used for                                   |
|----:|------------|----------|--------------------------------------------|
|   0 | (none)     | —        | identifier / label / function-name strings |
|   1 | u8         | 255 B    | sized blob                                  |
|   2 | u16        | 64 KB    | sized blob                                  |
|   3 | u32        | 4 GB     | sized blob                                  |
|   4 | u64        | 2^64 B   | sized blob                                  |

Tag 0 is terminator-encoded and used only for **name** strings, which are
guaranteed null-free. Everything sized — string literals, numbers, type
components — uses tags 1–4 (binary-safe). All size fields little-endian; the
encoder picks the smallest tag that fits.

---

## Section 3: Type Template Encoding

### 3.1 — Types are structural, never summed
**Status: Approved**

A type is preserved as its **ordered sequence of components**. Distinct
structures stay distinct across serialization: `.4+8 ≠ .12`, and
`.ptr+4 ≠ .4+ptr`.

Each component is stored in the pool as a constant (its raw bytes). A template
is encoded as a **null-terminated run of pool indices** — one index per
component, terminated by index 0.

| Template   | Encoded (pool indices)   |
|------------|--------------------------|
| `.4`       | `[ i(4), 0 ]`            |
| `.ptr`     | `[ i(ptr), 0 ]`          |
| `.ptr+ptr` | `[ i(ptr), i(ptr), 0 ]`  |
| `.4+8`     | `[ i(4), i(8), 0 ]`      |
| (absent)   | `[ 0 ]`                  |

`i(x)` = the pool index of the constant holding component `x`'s bytes. The
format does not interpret a component; it round-trips the bytes, and the
component sequence reconstructs exactly on decode.

---

## Section 4: Instruction Encoding

### 4.1 — Record layout
**Status: Approved**

```
+-------------+---------------+---------------+-----------------------+
| Opcode (1B) | Template0 seq | Template1 seq | Value indices (fixed) |
+-------------+---------------+---------------+-----------------------+
```

- **Opcode** — the op token's instruction byte (via the existing token-kind →
  instruction mapping). Non-op token kinds are never emitted.
- **Template0 / Template1** — each a null-terminated index run (Section 3.1).
- **Values** — a fixed number of pool indices, set by the opcode. No
  terminator: the count is known from the opcode.

| Source               | Encoded                                              |
|----------------------|------------------------------------------------------|
| `.4 add a b`         | `[Op_Add][i(4),0][0][i(a)][i(b)]`                    |
| `.ptr+ptr gep a b c` | `[Op_Gep][i(ptr),i(ptr),0][i(ptr),0][i(a)][i(b)][i(c)]` |
| `jmp label`          | `[Op_Jmp][0][0][i(label)]`                           |
| `.ptr set dest val`  | `[Op_Set][i(ptr),0][0][i(dest)][i(val)]`             |

### 4.2 — Pool index width (W) is global and adaptive
**Status: Approved**

A single index width applies to **every** index in the file, chosen from the
total pool count:

| Pool count | W       |
|------------|---------|
| < 256      | 1 byte  |
| < 65,536   | 2 bytes |
| < 2³²      | 4 bytes |
| otherwise  | 8 bytes |

Set once at encode time. The decoder infers W from the header's pool count — it
is not stored separately.

---

## Section 5: Symbol Table

### 5.1 — Purpose
**Status: InDiscussion**

A trailing region that lets a consumer look up exported symbols without
walking the instruction stream. It is a cache built from the same
function scan the analyzer already does — the instructions remain the source of
truth.

```
+------------+-------------+-----------------------+-----+
| Name (idx) | Return type | Param types (0+ runs) |  0  |
|            | [idx..][0]  | each: [idx..][0]      | end |
+------------+-------------+-----------------------+-----+
```

- **Name** — pool index of the function's name.
- **Return type** — a template index run (Section 3.1).
- **Param types** — zero or more template index runs, one per parameter.
- **Terminator** — a lone index `0` (an empty run) ends the parameter list. No
  explicit count is needed: a real parameter type is never empty, so a `0` at a
  run's start position marks the end.

The entry is self-contained — the full shape is readable from the table alone.
An entry point (instruction offset, for calls) is **deferred**: shape lookup
does not need it, and it can be appended later without disturbing the layout.

---

## Section 6: Implementation Structure

### 6.1 — Modules
**Status: InDiscussion**

Public surface stays in `Bytecode.h` (the container + the stream decoder + the
function-table lookup). The encoder pieces are internal to `liblan`.

| Module              | Responsibility                                                                       | Depends on                          |
|---------------------|--------------------------------------------------------------------------------------|-------------------------------------|
| InstructionTable    | Declarative per-op metadata: template + value counts and per-slot constraints (presence, kind, type-source, bind-mode). One source of truth for analyzer, encoder, decoder. | —                                   |
| ConstantPool        | Intern/dedup raw constants, assign 1-based indices, choose W, serialize pool.         | —                                   |
| TypeEncoder         | Emit a template's null-terminated index run by interning each component's bytes.      | ConstantPool                        |
| Encoder             | Two-pass AST walk: build pool, emit records, emit function table, prepend header.     | ConstantPool, TypeEncoder, InstructionTable |
| Bytecode (existing) | Public container over the flat buffer + streaming decoder + function-table lookup.    | Encoder                             |

**Interfaces (described, not coded):**
- **InstructionTable** — *shape(op) → template count, value count, per-slot constraints*.
- **ConstantPool** — *add(bytes) → index* (dedup), *count*, *width*, *serialize → region*.
- **TypeEncoder** — *encode(type token) → writes index run + terminator*.
- **Encoder** — *encode(module, version) → byte buffer*.
- **Bytecode** — *from AST*, *from bytes*, *free*, header getters, raw *bytes*/*count*; stream: *advance*, *opcode*, *template0/1*, *value0..3*; *find function(name) → shape*.

### 6.2 — Build order
**Status: InDiscussion**

`InstructionTable → ConstantPool → TypeEncoder → Encoder → Bytecode wiring`

Derived from the dependency arrows. InstructionTable and ConstantPool have no
project dependencies; TypeEncoder needs the pool; Encoder orchestrates all three;
the Bytecode container/decoder wraps the finished buffer last.

**Prerequisite note:** InstructionTable is also the data behind the analyzer.
Landing it entails refactoring `AnalyzerRules` from the `Must…/Should…` macros to
table-driven checks — a task in its own right, sequenced first so the analyzer,
encoder, and decoder share one definition and cannot drift.

---

## Section 7: Decisions

### 7.1 — Dedup key
**Status: Approved**

Constants dedup on their **full encoded entry bytes** (tag + size + data). Same
data under different tags stays distinct. The pool is deliberately typeless, so
identical entries collapsing to one is correct and maximizes sharing.

### 7.2 — Per-opcode counts & constraints
**Status: Approved**

Template count, value count, and per-slot constraints come from the single
declarative **InstructionTable** (Section 6.1), consumed by analyzer, encoder,
and decoder alike. No hand-written second table — that is the drift risk being
designed out.
