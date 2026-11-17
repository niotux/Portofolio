"""Command execution: generic tools and target-based tool propagation."""

import os
import subprocess
import sys
from pathlib import Path

from empire.core import MODULES_DIR, bashify_path, expand, empire_arch, empire_os, empire_rid
from empire.deps import copy_dep_to_target, dep_cwd, resolve_dep_flags
from empire.help import (
    print_tool_usage,
    print_tool_usage_configs,
    print_usage_with_missing,
    validate_help_args,
)
from empire.loaders import load_module, load_target
from empire.logging import die, log
from empire.models import Dep, Module, Option, Target, Tool


def run_bash_script(script_path: str, args: list, env: dict) -> None:
    cleaner_cmd = f"sed 's/\r$//' {script_path}"
    cleaner = subprocess.Popen(cleaner_cmd, shell=True, stdout=subprocess.PIPE)
    subprocess.run(
        ["bash", "/dev/stdin"] + list(args),
        stdin=cleaner.stdout,
        env=env,
    )


# ── generic (no target file) ─────────────────────────────────────────────────

def cmd_tool_generic(
    module: Module,
    tool: Tool,
    tool_name: str,
    args: list[str],
    cwd: Path,
) -> None:
    valid, missing = validate_help_args(tool, args)
    if not valid:
        print_usage_with_missing(module.name, tool, missing)
        sys.exit(1)

    base_env = {
        "EMPIRE_OS":         empire_os(),
        "EMPIRE_ARCH":       empire_arch(),
        "EMPIRE_RID":        empire_rid(),
        "EMPIRE_MODULE_DIR": str(MODULES_DIR / module.name),
    }
    base_env = {k: bashify_path(v) for k, v in base_env.items()}

    command = expand(f"{tool.command} {' '.join(args)}".strip(), base_env)
    log("info", f'Executing "{tool_name}"', command)

    result = subprocess.run(
        command,
        shell=True,
        cwd=str(cwd),
        env={**os.environ, **base_env},
    )
    if result.returncode != 0:
        sys.exit(result.returncode)
    print()


# ── target-based ─────────────────────────────────────────────────────────────

def cmd_tool_target(
    module: Module,
    tool: Tool,
    tool_name: str,
    args: list[str],
    cwd: Path,
    visited: set | None = None,
    top_out_dir: Path | None = None,
) -> None:
    if visited is None:
        visited = set()

    has_options = bool(tool.options)

    if has_options:
        if not args:
            print_tool_usage(module.name, tool, level="error")
            sys.exit(1)
        option_name = args[0]
        option = tool.option(option_name)
        if not option:
            die(
                f"Unknown option '{option_name}'",
                f"available: {', '.join(o.name for o in tool.options)}",
            )
        config_name = args[1] if len(args) > 1 else None
    else:
        option      = None
        option_name = ""
        config_name = args[0] if args else None

    target = load_target(cwd, module.name)

    require_config = option.require_config if option else tool.require_config
    if require_config and not config_name and target.configs:
        print_tool_usage_configs(module.name, tool, option, target)
        sys.exit(1)

    config = None
    if config_name:
        config = target.config(config_name)
        if not config:
            names = [c.name for c in target.configs]
            die(f"Unknown config '{config_name}'", f"available: {', '.join(names)}")

    rid     = empire_rid()
    src_dir = (cwd / target.source).resolve() if target.source else cwd

    base_env = {
        "EMPIRE_OS":            empire_os(),
        "EMPIRE_ARCH":          empire_arch(),
        "EMPIRE_RID":           rid,
        "EMPIRE_CONFIG":        config_name or "",
        "EMPIRE_MODULE_DIR":    str(MODULES_DIR / module.name),
        "EMPIRE_TARGET_NAME":   target.name,
        "EMPIRE_TARGET_SRC":    str(src_dir),
        "EMPIRE_FLAGS_CONFIG":  config.flags if config else "",
        "EMPIRE_FLAGS_LINKING": "",
    }

    out_base_raw = expand(target.output_base, base_env)
    out_base_dir = (cwd / out_base_raw).resolve()
    out_raw  = expand(target.output or "", base_env) if config_name else ""
    art_raw  = expand(target.artefacts or "", base_env) if config_name else ""
    out_dir  = (out_base_dir / out_raw).resolve() if out_raw else out_base_dir
    art_dir  = (out_base_dir / art_raw).resolve() if art_raw else out_base_dir

    base_env["EMPIRE_TARGET_OUTBASE"] = str(out_base_dir)
    base_env["EMPIRE_TARGET_OUT"]     = str(out_dir)
    base_env["EMPIRE_TARGET_ART"]     = str(art_dir)

    deps = target.dependencies
    base_env["EMPIRE_FLAGS_LINKING"] = resolve_dep_flags(
        deps, base_env, module.name, tool_name
    )

    propagate = option.propagate if option else tool.propagate

    # The top parent's output dir; None means we *are* the top caller.
    top_out = top_out_dir if top_out_dir is not None else out_dir

    # ── propagate to dependencies ──────────────────────────────────────────
    if propagate:
        for dep in deps:
            dep_module_name = dep.module or module.name
            dep_module      = load_module(dep_module_name)
            dep_tool        = dep_module.tool(tool_name)

            if not dep_tool:
                log(
                    "warn",
                    f"Skipping dep '{dep.path}'",
                    f"module '{dep_module_name}' has no tool '{tool_name}'",
                )
                continue

            handler = next((h for h in dep.on if h.tool == tool_name), None)
            if handler:
                dep_args = (
                    [handler.option, config_name]
                    if config_name else [handler.option]
                )
            else:
                dep_option = dep_tool.option(option_name) if option_name else None
                if option_name and not dep_option:
                    log(
                        "warn",
                        f"Skipping dep '{dep.path}'",
                        f"module '{dep_module_name}' has no option '{option_name}'",
                    )
                    continue
                dep_args = args

            dep_dir = dep_cwd(dep, base_env)
            dep_key = (
                str(dep_dir),
                dep_args[0] if dep_args else "",
                config_name or "",
            )
            if dep_key in visited:
                continue
            visited.add(dep_key)

            cmd_tool(
                dep_module,
                tool_name,
                dep_args,
                dep_dir,
                visited=visited,
                top_out_dir=top_out,
            )

            # Copy dep output to the top parent.
            copy_dep_to_target(
                dep, base_env, module.name, top_out, tool_name
            )

    # ── create output dirs ─────────────────────────────────────────────────
    create_dirs = option.create_dirs if option else tool.create_dirs
    if create_dirs:
        out_dir.mkdir(parents=True, exist_ok=True)
        art_dir.mkdir(parents=True, exist_ok=True)

    # ── build command and run ───────────────────────────────────────────────
    flags   = expand(option.flags if option else "", base_env)
    command = expand(f"{tool.command} {flags}".strip(), base_env)

    tag = " ".join(filter(None, [option_name, config_name, rid]))
    log(
        "info",
        f'Executing "{tool_name}"',
        f'{target.name}' + (f' | {tag}' if tag else ""),
    )
    log("info", "Command", command)

    # Safety: refuse to delete the project root or any ancestor
    if "rm" in command:
        for p in (out_base_dir, out_dir, art_dir):
            if str(p) == str(cwd):
                die(
                    f"Refusing to execute '{command}' — target would delete the project root"
                )
            try:
                p.relative_to(cwd)
            except ValueError:
                die(
                    f"Refusing to execute '{command}' — target would delete outside project root: {p}"
                )

    result = subprocess.run(
        command,
        shell=True,
        cwd=str(cwd),
        env={**os.environ, **base_env},
    )
    if result.returncode != 0:
        sys.exit(result.returncode)

    # ── copy deps skipped in propagation (no tool/option) ─────────────────
    # Only done at the top level (top_out_dir is None) to avoid double-copy.
    if top_out_dir is None:
        for dep in deps:
            dep_module_name = dep.module or module.name
            dep_tool        = load_module(dep_module_name).tool(tool_name)
            if dep_tool:
                # Had the tool — was handled in propagation.
                continue
            handler = next((h for h in dep.on if h.tool == tool_name), None)
            dep_option_name = handler.option if handler else ""
            dep_option = load_module(dep_module_name).option_anywhere(dep_option_name)
            if dep_option and dep_option.copy:
                copy_dep_to_target(
                    dep, base_env, module.name, out_dir, tool_name
                )

    log("info", "Output", str(out_dir))
    print()


# ── dispatcher ────────────────────────────────────────────────────────────────

def cmd_tool(
    module: Module,
    tool_name: str,
    args: list[str],
    cwd: Path,
    visited: set | None = None,
    top_out_dir: Path | None = None,
) -> None:
    tool = module.tool(tool_name)
    if not tool:
        names = [t.name for t in module.tools]
        die(f"Unknown tool '{tool_name}'", f"available: {', '.join(names)}")

    if module.type == "generic":
        cmd_tool_generic(module, tool, tool_name, args, cwd)
    else:
        cmd_tool_target(
            module,
            tool,
            tool_name,
            args,
            cwd,
            visited,
            top_out_dir=top_out_dir,
        )
