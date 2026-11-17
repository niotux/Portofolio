# Lan Implementation Plan

## Current Status

- **Tokenizing** → Tokens ✅ (working)
- **Parsing** → Raw AST ✅ (working)
- **Analyzing** → ✅ (working)
- **Emitting** → ❌ TODO

---

## Phase 1: Spec Updates

- [ ] Review `instructions.md` for consistency
- [ ] Document token format expectations
- [ ] Clarify edge cases

---

## Phase 2: Analyzer Library

**Location:** `libs/lan/Analyzer.c/h`

```
Raw AST ──► [Analyzer] ──► Validated AST
```

- [ ] Symbol table (track declared variables/labels)
- [ ] Validate op signatures (e.g., `add.32` needs exactly 2 operands)
- [ ] Validate size suffixes match operand contexts
- [ ] Report semantic errors

---

## Phase 3: LLVM Emitter Library

**Location:** `libs/lan2llvm/Emitter.c/h`

```
Validated AST ──► [Emitter] ──► LLVM IR
```

- [ ] Map Lan ops → LLVM instructions
- [ ] Handle size suffixes
- [ ] Control flow (labels → BasicBlocks)
- [ ] Options: target triple, optimization level, debug info

---

## Phase 4: CLI Tool

**Location:** `tools/lan2llvm/main.c`

```
lan2llvm [options] <input.lan> [-o output]
```

Options:
- `-o <file>` — output file
- `-t <target>` — target triple
- `-O0..3` — optimization level
- `-g` — debug info
- `--emit-llvm` — output LLVM IR instead of binary

---

## Final Directory Structure

```
libs/
├── clikit/          # CLI utilities
└── lan/
    ├── Tokenizer.c/h
    ├── Parser.c/h   # (existing, working)
    ├── Analyzer.c/h # NEW
    ├── String.c/h
    └── lan.h

libs/lan2llvm/       # NEW
    ├── lan2llvm.h
    └── Emitter.c/h

tools/lan2llvm/
    └── main.c       # (existing, refactor)
```