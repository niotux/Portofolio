"""ANSI terminal colour codes and helpers."""

ANSI = {
  "RESET":  "\033[0m",
  "RED":    "\033[31m",
  "GREEN":  "\033[32m",
  "YELLOW": "\033[33m",
  "GRAY":   "\033[90m",
  "BLUE":   "\033[34m",
}


def dim(s: str) -> str:
  return f"{ANSI['GRAY']}{s}{ANSI['RESET']}"


def red(s: str) -> str:
  return f"{ANSI['RED']}{s}{ANSI['RESET']}"


def grn(s: str) -> str:
  return f"{ANSI['GREEN']}{s}{ANSI['RESET']}"


def ylw(s: str) -> str:
  return f"{ANSI['YELLOW']}{s}{ANSI['RESET']}"


def blu(s: str) -> str:
  return f"{ANSI['BLUE']}{s}{ANSI['RESET']}"
