# Changelog

All notable changes to the "LAN Language Support" extension.

## [0.0.1] - Unreleased

### Added
- Initial grammar for `.lan` files
- Opcode highlighting, split into control flow, operations, and global declarations
- String literals with optional encoding suffix (`ascii`, `utf8`, `utf16`, `utf32`, `latin`)
- Numeric literals: integer, hex, octal, binary, double (`inf`, `neginf`, `nan`)
- Boolean literals
- Type expression highlighting (`.Type`, dotted identifiers, `+` unions, `[...]` generics)