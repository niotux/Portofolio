"""Dependency resolution: flag injection and artefact copying."""

from pathlib import Path

from empire.core import expand
from empire.logging import log
from empire.loaders import load_module, load_target
from empire.models import Dep


def dep_cwd(dep: Dep, base_env: dict) -> Path:
    return (Path(base_env["EMPIRE_TARGET_SRC"]) / dep.path).resolve()


def resolve_dep_flags(
    deps: list[Dep],
    base_env: dict,
    module_name: str,
    tool_name: str,
) -> str:
    flags = []
    for dep in deps:
        dep_module_name = dep.module or module_name
        dep_module      = load_module(dep_module_name)
        handler         = next((h for h in dep.on if h.tool == tool_name), None)
        dep_option      = dep_module.option_anywhere(
            handler.option if handler else ""
        )

        if not dep_option or not dep_option.linking:
            continue

        dep_dir      = dep_cwd(dep, base_env)
        dep_data     = load_target(dep_dir, dep_module_name)
        dep_src      = (dep_dir / dep_data.source).resolve()
        dep_out_base = (dep_dir / expand(dep_data.output_base, base_env)).resolve()
        dep_out      = (dep_out_base / expand(dep_data.output or "", base_env)).resolve()

        flag = (dep_option.linking
            .replace("{ROOT}", str(dep_dir))
            .replace("{SRC}",  str(dep_src))
            .replace("{OUT}",  str(dep_out))
            .replace("{NAME}", dep_data.name))

        flags.append(flag)

    return " ".join(flags)


def copy_dep_to_target(
    dep: Dep,
    base_env: dict,
    module_name: str,
    top_out_dir: Path,
    tool_name: str,
) -> None:
    """Copy a single dependency's output into the top-level output directory."""
    import shutil

    dep_module_name = dep.module or module_name
    dep_module      = load_module(dep_module_name)
    handler         = next((h for h in dep.on if h.tool == tool_name), None)
    dep_option      = dep_module.option_anywhere(
        handler.option if handler else ""
    )

    if not dep_option or not dep_option.copy:
        return

    dep_dir      = dep_cwd(dep, base_env)
    dep_data     = load_target(dep_dir, dep_module_name)
    dep_out_base = (dep_dir / expand(dep_data.output_base, base_env)).resolve()
    dep_out      = (dep_out_base / expand(dep_data.output or "", base_env)).resolve()
    dep_art      = (dep_out_base / expand(dep_data.artefacts or "", base_env)).resolve()

    if not dep_out.exists():
        return

    copied = set()
    for item in dep_out.rglob("*"):
        if not item.is_file():
            continue
        if dep_art.exists() and dep_art in item.parents:
            continue

        src_key = str(item.resolve())
        if src_key in copied:
            continue
        copied.add(src_key)

        rel_path = item.relative_to(dep_out)
        dst = top_out_dir / rel_path
        dst.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(item, dst)
        log("info", f"Copied {item.name}", str(dst.parent))
