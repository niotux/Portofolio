"""CLI usage and help formatting."""

from __future__ import annotations

import sys
from pathlib import Path
from typing import TYPE_CHECKING

# Ensure empire/ is importable from cli/
_project_root = Path(__file__).resolve().parent.parent
if str(_project_root) not in sys.path:
    sys.path.insert(0, str(_project_root))

if TYPE_CHECKING:
    from empire.models import Module, Tool, Option, Target

from empire.ansi import blu, dim, grn, red, ylw
from empire.io import error, flush, info
from empire.loaders import available_modules, load_module


def print_global_help() -> None:
    """Print the global Empire usage screen."""
    mods = available_modules()
    print(f"""
  {grn('EMPIRE')}

    {blu('Usage:')}
      empire <module> <tool> [options] [config]
      empire <module> [-h | --help]
      empire -r|--runtime <name>   select frontend

    {blu('Modules:')}
      {('  ').join(ylw(m) for m in mods) if mods else 'No modules installed'}

    {blu('Examples:')}
      empire c build -exe debug
      empire c build -dylib release
      empire cs build -lib release
  """)


def print_runtime_help(runtimes: dict, default: str | None = None) -> None:
    """Print usage info for the -r|--runtime flag."""
    info("Runtime selection (-r|--runtime)")
    info("  empire -r|--runtime <name>   select frontend")
    info("")
    for name, cfg in runtimes.items():
        label = cfg.get("Label", name.title())
        tag   = f"  (default)" if name == default else ""
        info(f"  {name:<10}  {label}{tag}")
    flush()


def _print_usage(level: str, subject: str, text: str) -> None:
    print(f"    empire {subject} {red(text) if level == 'error' else grn(text)}")


def print_module_help(module: Module) -> None:
    """Print help for a module's tools and options."""
    col = 36
    info("Module usage", module.name)
    print()
    print(blu("  Tools:"))

    for tool in module.tools:
        if tool.help_args:
            # Tool with args (like new, init)
            prop  = f"  {dim('[propagates]')}" if tool.propagate else ""
            cfg   = " <config>" if tool.require_config else ""
            usage = tool.name + " " + format_help_args(tool.help_args)
            if cfg:
                usage += cfg
            _print_usage("info", module.name, f"{usage:<{col}}{prop}")
        elif tool.options:
            # Tool with options (like build, clean)
            for o in tool.options:
                prop  = f"  {dim('[propagates]')}" if o.propagate else ""
                cfg   = " <config>" if o.require_config else ""
                usage = f"{tool.name} {o.name}{cfg}"
                _print_usage("info", module.name, f"{usage:<{col}}{prop}")
        else:
            # Simple tool
            prop  = f"  {dim('[propagates]')}" if tool.propagate else ""
            cfg   = " <config>" if tool.require_config else ""
            usage = tool.name
            if cfg:
                usage += cfg
            _print_usage("info", module.name, f"{usage:<{col}}{prop}")
    print()


def print_tool_usage(module: "Module | str", tool: Tool | None, level: str = "info") -> None:
    """Print usage for a specific tool."""
    from empire.models import Module as _Module
    name = module.name if isinstance(module, _Module) else module
    if not tool:
        error("Tool name is required for usage.")
        flush()
        return

    info("Tool usage", f"{name}::{tool.name}")
    if tool.help_args:
        print(blu("\n  Args:"))
        _print_usage(level, f"{name} {tool.name}", format_help_args(tool.help_args))
    elif tool.options:
        print(blu("\n  Options:"))
        for o in tool.options:
            cfg = " <config>" if o.require_config else ""
            _print_usage(level, f"{name} {tool.name}", f"{o.name}{cfg}")
    print()



def print_usage_with_missing(module: Module, tool: Tool, missing: list[str]) -> None:
    """Print usage with missing arguments highlighted."""
    import re
    error("Tool usage", f"{module.name}::{tool.name}")
    print(blu("\n  Args:"))
    full = format_help_args(tool.help_args)
    for name in sorted(missing, key=len, reverse=True):
        full = re.sub(rf'(<{re.escape(name)}>)', red(r'\1'), full)
        full = re.sub(rf'(\[.{0,20}?<{re.escape(name)}>)', red(r'\1'), full)
    print(f"    empire {module.name} {tool.name} {full}")
    print()


def print_tool_usage_configs(name: str, tool: Tool | None, option: object, target: object) -> None:
    """Print config options for a tool."""
    if not tool:
        error("Tool name is required for usage.")
        flush()
        return

    error("Tool usage", f"{name}::{tool.name}")
    print(blu("\n  Configs:"))
    for c in target.configs:
        _print_usage("error", f"{name} {tool.name} {option.name}", c.name)
    print()


# ── Format helpers ─────────────────────────────────────────────────────────────

def format_help_args(help_args: list) -> str:
    """Format a Help args list into a usage string."""
    fixed_req: list[str] = []
    fixed_opt: list[str] = []
    repeating: list[str] = []

    for arg in help_args:
        opt_marker = arg.get("Optional")
        repeat     = arg.get("Repeat", False)

        if opt_marker is not None:
            formatted = f"[{opt_marker} <{arg['Arg']}>"
        else:
            formatted = f"<{arg['Arg']}>"

        if repeat:
            repeating.append(formatted)
        elif opt_marker is not None:
            fixed_opt.append(formatted)
        else:
            fixed_req.append(formatted)

    parts = fixed_req + fixed_opt
    if repeating:
        parts.append(f"({', '.join(repeating)})")
    return " ".join(parts)


def validate_help_args(tool: Tool, args: list) -> tuple[bool, list[str]]:
    """Validate args against the tool's Help schema. Returns (valid, missing_names)."""
    if not tool.help_args:
        return True, []

    fixed_req: list[dict] = []
    rep_args: list[dict]  = []

    for h in tool.help_args:
        is_opt = h.get("Optional") is not None
        is_rep = h.get("Repeat", False)
        if is_rep:
            rep_args.append(h)
        elif not is_opt:
            fixed_req.append(h)

    missing: list[str] = []

    if len(args) < len(fixed_req):
        for h in fixed_req[len(args):]:
            missing.append(h["Arg"])
        if rep_args:
            missing.extend(h["Arg"] for h in rep_args)
        return False, missing

    provided_rep_count = len(args) - len(fixed_req)
    rep_req_count = sum(1 for h in rep_args if h.get("Optional") is None)

    if provided_rep_count < rep_req_count or (rep_args and (provided_rep_count % len(rep_args)) != 0):
        if provided_rep_count == 0:
            missing.extend(h["Arg"] for h in rep_args)
        else:
            group_size = len(rep_args)
            remainder = provided_rep_count % group_size
            for i, h in enumerate(rep_args):
                if i >= remainder:
                    missing.append(h["Arg"])
        return False, missing

    return True, []
