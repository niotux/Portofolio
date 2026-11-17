"""Module and target file loading with caching."""

from pathlib import Path

from empire.core import MODULES_DIR, load_json
from empire.logging import die
from empire.models import Module, Target

_module_cache: dict[str, Module] = {}
_target_cache: dict[tuple, Target] = {}

# Standard tools for target modules (new, init)
_STD_TOOLS_PATH = Path(__file__).parent / "std_tools.json"


def available_modules() -> list[str]:
    if not MODULES_DIR.exists():
        return []
    return sorted(
        d.name for d in MODULES_DIR.iterdir()
        if d.is_dir() and (d / "module.json").exists()
    )


def load_module(name: str) -> Module:
    if name in _module_cache:
        return _module_cache[name]

    path = MODULES_DIR / name / "module.json"
    if not path.exists():
        die(
            f"Unknown module '{name}'",
            f"available: {', '.join(available_modules())}",
        )

    m = Module.from_dict(name, load_json(path))

    # Inject standard target tools for target modules
    if m.type == "target" and _STD_TOOLS_PATH.exists():
        std_tools = load_json(_STD_TOOLS_PATH)
        for tool_def in std_tools.get("Tools", []):
            # Skip if already defined
            if not m.tool(tool_def["Name"]):
                from empire.models import Tool
                tool = Tool.from_dict(tool_def)
                m.tools.append(tool)
                m._tools[tool.name] = tool

    _module_cache[name] = m
    return m


def load_target(cwd: Path, module_name: str) -> Target:
    key = (str(cwd), module_name)
    if key in _target_cache:
        return _target_cache[key]

    path = cwd / f"target.{module_name}.json"
    if not path.exists():
        die(
            f"Target file 'target.{module_name}.json' not found",
            f"tried: {path}",
        )

    t = Target.from_dict(load_json(path))
    _target_cache[key] = t
    return t
