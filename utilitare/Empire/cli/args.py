"""CLI argument parsing and dispatch."""

from __future__ import annotations

import sys
from pathlib import Path

from empire.io import error, flush, info, warn
from empire.loaders import load_module
from empire.models import Module


def parse_args(args: list[str]) -> dict:
    """Parse CLI arguments into a dispatch dict.

    Returns a dict with keys:
      - 'module_name': str | None
      - 'tool_name': str | None
      - 'tool_args': list[str]
      - 'help': bool (global, module, tool, runtime)
      - 'runtime': str | None
    """
    result = {
        'module_name': None,
        'tool_name': None,
        'tool_args': [],
        'help': False,
        'runtime': None,
    }

    # Handle runtime flag
    if args and args[0] in ('-r', '--runtime'):
        if len(args) >= 2:
            result['runtime'] = args[1]
        else:
            result['runtime'] = None  # missing argument
        return result

    if not args or args[0] in ('-h', '--help'):
        result['help'] = True
        return result

    result['module_name'] = args[0]
    rest = args[1:]

    if not rest or rest[0] in ('-h', '--help'):
        result['help'] = True
        return result

    result['tool_name'] = rest[0]
    tool_args = rest[1:]

    if tool_args and tool_args[0] in ('-h', '--help'):
        result['help'] = True
        result['tool_args'] = tool_args[1:]
        return result

    result['tool_args'] = tool_args
    return result


def dispatch(args: list[str]) -> None:
    """Parse arguments and dispatch to the appropriate action."""
    cwd = Path.cwd()
    parsed = parse_args(args)

    # Global help
    if parsed['help'] and not parsed['module_name']:
        from cli.usage import print_global_help
        print_global_help()
        return

    # Runtime help
    if parsed['runtime'] is None and parsed.get('_runtime_flag', False):
        from cli.usage import print_runtime_help
        from empire.core import load_settings
        settings = load_settings()
        runtimes = settings.get('Runtimes', {})
        print_runtime_help(runtimes)
        return

    module_name = parsed['module_name']
    if not module_name:
        error("Module name is required.")
        flush()
        sys.exit(1)

    # Load module
    try:
        module = load_module(module_name)
    except SystemExit:
        raise  # re-raise from loader

    # Module help
    if parsed['help'] and not parsed['tool_name']:
        from cli.usage import print_module_help
        print_module_help(module)
        return

    tool_name = parsed['tool_name']
    if not tool_name:
        from cli.usage import print_tool_usage
        print_tool_usage(module, None)
        return

    # Tool help
    if parsed['help']:
        tool = module.tool(tool_name)
        if tool:
            from cli.usage import print_tool_usage
            print_tool_usage(module, tool)
        else:
            error(f"Unknown tool '{tool_name}'")
            flush()
        return

    # Dispatch to action
    if tool_name in ('new', 'init'):
        if module.type != 'target':
            error(f"Module '{module_name}' is generic and has no '{tool_name}' command")
            flush()
            sys.exit(1)

        from cli.usage import print_tool_usage
        # Validate required args before dispatching
        if tool_name == 'new' and not parsed['tool_args']:
            print_tool_usage(module, module.tool('new'), level='error')
            return

        from empire.commands import cmd_init, cmd_new
        if tool_name == 'new':
            cmd_new(module, parsed['tool_args'], cwd)
        else:
            cmd_init(module, parsed['tool_args'], cwd)
    else:
        from empire.executors import cmd_tool
        cmd_tool(module, tool_name, parsed['tool_args'], cwd)
