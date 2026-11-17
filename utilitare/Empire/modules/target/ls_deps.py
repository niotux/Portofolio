"""empire target ls-deps"""

import sys

import target_utils as U


def main() -> None:
    data, _ = U.load_target()
    deps = data.get("Dependencies", [])

    if not deps:
        U.log_info("No dependencies", "file")
        sys.exit(0)

    print()
    print(f"  Dependencies ({len(deps)}):")

    for dep in deps:
        on = dep.get("On", [])
        if on:
            handlers = ", ".join(f"{h['Tool']}:{h['Option']}" for h in on)
        else:
            handlers = "(no handlers)"
        print(f"    {dep['Path']}  {dep.get('Module', '')}  {handlers}")

    print()


if __name__ == "__main__":
    main()
