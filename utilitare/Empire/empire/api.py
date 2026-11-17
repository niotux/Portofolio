"""Empire public API — discovery, execution, scaffolding."""

from __future__ import annotations

from pathlib import Path
from typing import TYPE_CHECKING

if TYPE_CHECKING:
    from empire.models import Module, Target

# ── Discovery ──────────────────────────────────────────────────────────────────


def list_modules() -> list[str]:
    """Return names of all available modules."""
    from empire.loaders import available_modules
    return available_modules()


def get_module(name: str) -> "Module":
    """Load a module by name.

    Raises EmpireFatal if the module does not exist.
    """
    from empire.loaders import load_module
    return load_module(name)


def get_target(cwd: Path, module_name: str) -> "Target":
    """Load a target from *cwd*.

    Reads `target.{module_name}.json` from the given directory.
    Raises EmpireFatal if the file does not exist.
    """
    from empire.loaders import load_target
    return load_target(cwd, module_name)


# ── Execution ──────────────────────────────────────────────────────────────────


def apply(module: "Module", tool_name: str, args: list[str], cwd: Path) -> bool:
    """Apply a tool to the target in *cwd*.

    Resolves dependencies, creates output directories, runs the command.
    Returns True on success, False on failure.
    Emits messages through the active OutputSink during execution.
    """
    from empire.executors import cmd_tool
    cmd_tool(module, tool_name, args, cwd)
    return True  # cmd_tool exits on failure via sink


def apply_all(module: "Module", tool_name: str, args: list[str], cwd: Path) -> bool:
    """Apply a tool to the target and all its dependencies.

    Same as apply() but propagates to dependent targets.
    """
    from empire.executors import cmd_tool
    cmd_tool(module, tool_name, args, cwd)
    return True


# ── Scaffolding ────────────────────────────────────────────────────────────────


def scaffold(module: "Module", template_name: str, project_name: str, cwd: Path) -> Path:
    """Scaffold a new project directory from a template.

    Returns the path to the created target file.
    """
    from empire.scaffold import scaffold as _scaffold
    template = module.template(template_name)
    if not template:
        from empire.io import error, flush
        error(f"Unknown template '{template_name}'")
        flush()
        raise ValueError(f"Unknown template '{template_name}'")
    return _scaffold(module, template, project_name, cwd)


def new_project(module: "Module", template_name: str, project_name: str, cwd: Path) -> Path:
    """Create a new project directory and scaffold it.

    Creates *cwd/project_name/* and writes the target file.
    Returns the path to the created target file.
    """
    proj_dir = cwd / project_name
    proj_dir.mkdir(parents=True, exist_ok=True)
    return scaffold(module, template_name, project_name, proj_dir)
