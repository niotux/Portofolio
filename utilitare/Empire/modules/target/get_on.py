"""empire target get-on <path>"""

import sys

import target_utils as U


def main() -> None:
    if len(sys.argv) < 2:
        U.log_error("Usage: empire target get-on <path>")
        sys.exit(1)

    dep_path_str = sys.argv[1]
    data, _ = U.load_target()

    for dep in data.get("Dependencies", []):
        if dep["Path"] == dep_path_str:
            on = dep.get("On", [])
            if not on:
                U.log_warn("No handlers found", f"path: {dep_path_str}")
                sys.exit(0)

            print()
            print(f"  Handlers for '{dep_path_str}':")
            for h in on:
                print(f"    {h['Tool']} -> {h['Option']}")
            print()
            sys.exit(0)

    U.log_warn("Dependency not found", f"path: {dep_path_str}")
    sys.exit(0)


if __name__ == "__main__":
    main()
