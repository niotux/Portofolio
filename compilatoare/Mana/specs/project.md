# Mana Project Specification

## Overview

Mana is a programming language designed to be a "universal" low-level systems language with high-level construct access.

## Core Philosophy

- **No syntactic sugar** - everything is explicit
- **No compiler magic** - all mechanisms are defined in the language itself
- **Int32 is a real type** - arithmetic and bitwise operations are implemented in Mana, not hardcoded in the compiler

## Two-Language Architecture

### Lan (Linear Assembly Notation)

A minimal assembly-like language for extremely low-level control.

- **Standalone only** - no inline syntax (yet)
- **No syscalls** - not portable across platforms
- **No I/O** - can only manipulate memory
- **No functions** - flat code with jumps for control flow
- **Simple and portable** - compiles to C, LLVM IR, or native code

**Instruction set highlights:**
- Arithmetic: add, sub, mul, div, mod
- Bitwise: and, or, xor, shl, shr
- Memory: alloc, load, store, gep (get element pointer)
- Control flow: labels, jumps, conditional branches
- Type conversion: casts between primitive and composite types

### Mana

The high-level (but still low-level) language.

- **Paradigm**: Similar to C#/D (OOP, interfaces, etc.) but simpler
- **Design inspiration**: Delphi with explicit memory/resource control
- **Value vs Reference types**: Semantics are opt-in with explicit allocation directives
- **Runtime metadata**: Can include runtime type metadata for serialization when needed
- **Interoperability**: Designed for direct interoperability with other languages
- **Will embed Lan** - for inline low-level control blocks

## Target Audience

- Low-level developers who want higher-level constructs without abstraction overhead
- Enterprises wanting fast native executables with simplicity

## Key Design Principles

- Close to bare metal
- Easy to interoperate with other languages
- Explicit over implicit
- No extra complexity unless externally added
- All core operations (Int32.add, Int32.sub, bitwise ops) are defined in the language itself
- Simplicity first - Lan has no functions, syscalls, or I/O
