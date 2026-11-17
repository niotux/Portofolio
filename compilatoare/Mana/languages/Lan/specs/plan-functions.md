# Plan: Functions in LAN

## Language model (locked)

- The instruction stream is **flat and linear** — no nested blocks or scopes.
- A function is introduced by `fun .RetType Name;`.
- A function's **body** is every instruction from its `fun` declaration to the
  next `fun` (or end of stream).
- A function's **parameters** are the `ldarg` instructions immediately
  following its `fun` declaration. `ldarg` is only valid in that position.
- **Top-level / main:** a synthetic `fun .4 main;` is prepended to the token
  stream before parsing. Main therefore becomes an ordinary function — the
  analyzer has **no special-casing** for it.
- The `entry` op is **removed**. Top-level code is the entry, like Python.
- `ret` is a **single uniform form**: it checks its value against the current
  function's return type. There is no separate top-level vs. function variant.

## 1. Docs

Update `grammar.md` and `instructions.md`:

- `$` and `.` are valid identifier continuation characters.
- Composite type syntax: `.ptr+ptr`, `.4+8` — sizes add up.
- Instructions: `fun`, `call`, `arg`, and the uniform `ret`.
- Remove every mention of `entry`.
- State the synthetic `fun .4 main;` model for top-level code.

## 2. Tokenizer (done)

Implemented and verified:

- `$` and `.` as identifier continuations.
- Single-token templates, including composites (`.ptr+ptr`, `.4+8`);
  `DataBuffer` holds the **summed** size; `.ptr` = pointer size (8).
- Keywords `fun`, `call`, `arg` (and `entry` to be removed).
- Numeric literals classify correctly; malformed templates, stray markers,
  unterminated strings, and unknown characters are all reported.

Remaining tokenizer-adjacent work:

- **Synthetic main injection** — before parsing, prepend the four tokens
  `fun`, `.4`, `main`, `;` to the token array. The parser and analyzer then
  treat main like any other function with zero extra code paths.

## 3. Data structures (locked)

### Generic List — `libs/lan/List.{c,h}`

A growable list of `void*` with a **stable handle**:

```
struct LANList { size_t Count; size_t Capacity; void** Items; };
```

- The handle struct stays put; only `Items` is reallocated on growth, so the
  list may be stored in locals or other structs and references stay valid.
- API: make, free, push(`void*`), at(index), count, clear.
- This **replaces** the current `IdList.c` layout (header + inline array,
  reallocated in place), which has a real bug: `LANIdList_CheckAdd` reallocs
  the block and updates only its local pointer, dangling the caller's pointer
  after the first growth past capacity.

### `LANIdList` — built on three `LANList`s

- Holds three generic lists: **Funs**, **Ids**, and **Labels**, cleared
  independently. Funs persists for the whole module; Ids and Labels are both
  cleared at each new function (both are function-scoped).
- Entries are pointers — no separate metadata structs:
  - **Funs entry** = pointer to the `fun` instr. Name = its value arg,
    return type = its template token, plus source location for diagnostics.
  - **Ids entry** = the declaring tokens (name token + template token); the
    size is read from the template token's `DataBuffer`.
  - **Labels entry** = the `label` name token (location included).
- Clean API that hides the `instr*`:
  - `RegisterFun(list, funInstr)`
  - `CheckAddId(list, token, typeSize)` (current semantics)
  - `RegisterLabel(list, token)` / `ResolveLabel(list, token)`
  - `ClearIds(list)` / `ClearLabels(list)`
  - `ResolveFun(list, nameToken)` → `fun` instr or `NULL`
  - `CheckFunRetType(list, valueToken)` — value vs. current function's return
  - parameter access — internally steps `funInstr + 1, +2, …` across trailing
    `ldarg`s to yield the function's parameter count and types
- Because functions and labels live in their own lists, the earlier `-1`
  "function ID" and `-2` "label" type sentinels are no longer needed.

## 4. Analyzer (locked) — two passes

The current stateless dispatch (`AnalyzerDelegations[kind](instr)`) gains a
**context** carrying the `LANIdList`, the current `fun` instr, and the module
cursor. Handlers may advance the shared cursor.

**Pass 1 — collect funs.** Walk the stream; for each `fun`, `RegisterFun`.
This makes forward references in `call` legal across the whole module.

**Pass 2 — analyze.** Walk the stream tracking the current function (the first
instruction is the synthetic `fun .4 main`). On entering each function, first
**pre-scan its body** (up to the next `fun`) and `RegisterLabel` every `label`,
so forward jumps within the function validate. Then analyze its instructions:

- **`fun`** → set current function; `ClearIds`; `ClearLabels`; pre-scan and
  register this function's labels.
- **`ldarg`** → must be immediately after a `fun` (a parameter); add to Ids.
  Anywhere else → error.
- **`label`** → already registered by the pre-scan; declaring a duplicate
  label in the same function → error.
- **`jmp` / conditional jumps** → target must `ResolveLabel` in the current
  function; not found → error. (Unifies today's inconsistent `jmp`=must-new
  vs. conditional=must-match behavior into: any jump target must exist.)
- **`ret`** → `CheckFunRetType(value0)` against the current function; mismatch
  → error.
- **`call A → dest`**:
  - `ResolveFun(A)`; not found → error.
  - `dest` must be an existing id whose type matches A's return type; else error.
  - consume the following `arg` instructions, matching each positionally
    against A's parameters (value must be an existing id or a literal of
    matching size). More args than params → **too many arguments**; params
    left when args end → **too few arguments**.
- All other ops → unchanged from current behavior.

Replace the four placeholder dispatch entries; remove `Op_Entry`. Delete the
`Analyzer.c` file-scope globals in favour of the `LANIdList` context.

## 5. Build sequence

1. **Generic `LANList`** — no project dependencies.
2. **`LANIdList`** on top of `LANList` (Funs + Ids + Labels lists + API).
   Replaces the current `IdList.c` and the `Analyzer.c` globals.
3. **Remove `entry`** — `Keywords.h`, the `Op_Entry` enum value and its
   `LANTokenKind_Name` case, the dispatch entry, and the doc examples.
4. **Synthetic main** — prepend `fun .4 main ;` tokens before parsing.
5. **Analyzer** — pass 1 (collect) + pass 2 (context walk with real `fun`,
   `ldarg`, `ret`, `call`/`arg` analyzers); wire dispatch; delete globals.
6. **Tests** — one fixed input → expected-output case per behavior above.

## Open questions

- None. The `-1` (function) and `-2` (label) type sentinels are confirmed
  dropped — funs and labels each live in their own list.
