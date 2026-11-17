"""CLI sub-commands: init, new."""

from pathlib import Path

from empire.logging import die, log
from empire.models import Module
from empire.scaffold import parse_template_arg, scaffold


def cmd_init(module: Module, args: list[str], cwd: Path) -> None:
    default     = module.templates[0].name if module.templates else None
    tmpl_name, _ = parse_template_arg(args, default=default)
    proj_name   = cwd.name

    template = module.template(tmpl_name)
    if not template:
        names = [t.name for t in module.templates]
        die(
            f"Unknown template '{tmpl_name}'",
            f"available: {', '.join(names)}",
        )

    target_path = scaffold(module, template, proj_name, cwd)

    log("info", f'Project "{proj_name}" initialised', tmpl_name)
    log("info", "Target", str(target_path))


def cmd_new(module: Module, args: list[str], cwd: Path) -> None:
    if not args or args[0].startswith("-"):
        die(f"Usage: empire {module.name} new <name> [-t <template>]")

    proj_name = args[0]
    proj_dir  = cwd / proj_name
    proj_dir.mkdir(parents=True, exist_ok=True)

    cmd_init(module, args[1:], proj_dir)
