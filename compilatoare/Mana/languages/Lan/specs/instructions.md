# Lan Instructions Cheat Sheet

## Constants & Assignment

| Instruction | Template | Args | Description |
|-------------|----------|------|-------------|
| `set` | `.N` | `dest value` | Write literal `value` into `dest` |

---

## Arithmetic

| Instruction | Template | Args | Description |
|-------------|----------|------|-------------|
| `add` | `.N` | `dest src1 src2` | `dest` = `src1` + `src2` |
| `sub` | `.N` | `dest src1 src2` | `dest` = `src1` - `src2` |
| `mul` | `.N` | `dest src1 src2` | `dest` = `src1` * `src2` |
| `div` | `.N` | `dest src1 src2` | `dest` = `src1` / `src2` (unsigned) |
| `mod` | `.N` | `dest src1 src2` | `dest` = `src1` % `src2` (unsigned) |
| `idiv` | `.N` | `dest src1 src2` | `dest` = `src1` / `src2` (signed) |
| `imod` | `.N` | `dest src1 src2` | `dest` = `src1` % `src2` (signed) |
| `fadd` | `.N` | `dest src1 src2` | `dest` = `src1` + `src2` (float) |
| `fsub` | `.N` | `dest src1 src2` | `dest` = `src1` - `src2` (float) |
| `fmul` | `.N` | `dest src1 src2` | `dest` = `src1` * `src2` (float) |
| `fdiv` | `.N` | `dest src1 src2` | `dest` = `src1` / `src2` (float) |
| `fmod` | `.N` | `dest src1 src2` | `dest` = `src1` % `src2` (float) |

---

## Bitwise

| Instruction | Template | Args | Description |
|-------------|----------|------|-------------|
| `and` | `.N` | `dest src1 src2` | `dest` = `src1` & `src2` |
| `or` | `.N` | `dest src1 src2` | `dest` = `src1` | `src2` |
| `xor` | `.N` | `dest src1 src2` | `dest` = `src1` ^ `src2` |
| `shl` | `.N` | `dest src1 src2` | `dest` = `src1` << `src2` (logical) |
| `shr` | `.N` | `dest src1 src2` | `dest` = `src1` >> `src2` (logical, zero-fill) |
| `ishr` | `.N` | `dest src1 src2` | `dest` = `src1` >> `src2` (arithmetic, sign-extend) |
| `not` | `.N` | `dest src` | `dest` = ~`src` |

---

## Memory

| Instruction | Template | Args | Description |
|-------------|----------|------|-------------|
| `alloc` | `.N` | `ptr` | Allocate `N`-byte buffer on stack, stores adress in `ptr` |
| `write` | `.N` | `ptr value` | Write `value` to memory at `ptr` |
| `read` | `.N` | `dest ptr` | Read `N` bytes from `ptr` into `dest` |
| `gep` | `.N` | `dest ptr arrayIndex fieldIndex` | `dest` = pointer to `fieldIndex`'th field of the `arrayIndex`'th element starting from `ptr` of type `N` |

---

## Conversion

| Instruction | Templates | Args | Description |
|-------------|-----------|------|-------------|
| `conv` | `.N .M` | `dest src` | `src` (`M` uint bytes) → `dest` (`N` uint bytes) |
| `iconv` | `.N .M` | `dest src` | `src` (`M` int bytes) → `dest` (`N` int bytes) |
| `fconv` | `.N .M` | `dest src` | `src` (`M` float bytes) → `dest` (`N` float bytes) |
| `itof` | `.N .M` | `dest src` | `src` (`M` int bytes) → `dest` (`N` float bytes) |
| `utof` | `.N .M` | `dest src` | `src` (`M` uint bytes) → `dest` (`N` float bytes) |
| `ftoi` | `.N .M` | `dest src` | `src` (`M` float bytes) → `dest` (`N` int bytes) |
| `ftou` | `.N .M` | `dest src` | `src` (`M` float bytes) → `dest` (`N` uint bytes) |

---

## Control Flow

| Instruction | Template | Args | Description |
|-------------|----------|------|-------------|
| `label` | — | `name` | Define jump target |
| `jmp` | — | `label` | jump to `label` |
| `jeq` | `.N` | `label val1 val2` | Jump to `label` if `val1` == `val2` |
| `jne` | `.N` | `label val1 val2` | Jump to `label` if `val1` != `val2` |
| `jlt` | `.N` | `label val1 val2` | Jump to `label` if `val1` < `val2` (unsigned) |
| `jgt` | `.N` | `label val1 val2` | Jump to `label` if `val1` > `val2` (unsigned) |
| `jle` | `.N` | `label val1 val2` | Jump to `label` if `val1` <= `val2` (unsigned) |
| `jge` | `.N` | `label val1 val2` | Jump to `label` if `val1` >= `val2` (unsigned) |
| `ijlt` | `.N` | `label val1 val2` | Jump to `label` if `val1` < `val2` (signed) |
| `ijgt` | `.N` | `label val1 val2` | Jump to `label` if `val1` > `val2` (signed) |
| `ijle` | `.N` | `label val1 val2` | Jump to `label` if `val1` <= `val2` (signed) |
| `ijge` | `.N` | `label val1 val2` | Jump to `label` if `val1` >= `val2` (signed) |
| `fjlt` | `.N` | `label val1 val2` | Jump to `label` if `val1` < `val2` (float) |
| `fjgt` | `.N` | `label val1 val2` | Jump to `label` if `val1` > `val2` (float) |
| `fjle` | `.N` | `label val1 val2` | Jump to `label` if `val1` <= `val2` (float) |
| `fjge` | `.N` | `label val1 val2` | Jump to `label` if `val1` >= `val2` (float) |

---

## Functions

| Instruction | Template | Args | Description |
|-------------|----------|------|-------------|
| `fun` | `.N` | `name` | Declare function with returning `N` bytes |
| `call` | — | `func dest` | Call function `func`, store return result in `dest` |
| `arg` | — | `value` | Passes `value` as argument to the preceding `call` instruction |
| `ldarg` | `.N` | `dest` | Declares and loads function argument into `dest` |
| `ret` | — | `value` | Returns `value` from current function |

---