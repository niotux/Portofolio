"""Template argument parsing and project scaffolding."""

import json
from pathlib import Path

from empire.logging import die
from empire.models import Module, Template


def parse_template_arg(args: list[str], default: str | None = None) -> tuple[str | None, list[str]]:
    """Extract -t / --template <name> from args. Returns (template_name, remaining_args)."""
    args = list(args)
    for flag in ("-t", "--template"):
        if flag in args:
            i = args.index(flag)
            if i + 1 >= len(args):
                die(f"Flag '{flag}' requires a name")
            tmpl = args[i + 1]
            del args[i:i + 2]
            return tmpl, args
    return default, args


def scaffold(
    module: Module,
    template: Template,
    proj_name: str,
    proj_dir: Path,
) -> Path:
    """Write a target JSON file from a template into *proj_dir*."""
    target_data = {
        "Name":         proj_name,
        "Dependencies": template.dependencies,
        "Source":       template.source,
        "OutputBase":   template.output_base,
        "Output":       template.output,
        "Artefacts":    template.artefacts,
        "Configs":      [{"Name": c.name, "Flags": c.flags} for c in template.configs],
    }

    target_path = proj_dir / f"target.{module.name}.json"
    with open(target_path, "w") as f:
        json.dump(target_data, f, indent=2)
        f.write("\n")

    return target_path
