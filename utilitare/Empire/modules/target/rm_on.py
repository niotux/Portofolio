"""empire target rm-on <path> <tool>"""

import sys

import target_utils as U


def main() -> None:
    if len(sys.argv) < 3:
        U.log_error("Usage: empire target rm-on <path> <tool>")
        sys.exit(1)

    dep_path_str = sys.argv[1]
    dep_tool = sys.argv[2]
    data, tf = U.load_target()

    found_dep = False
    found_handler = False

    for dep in data.get("Dependencies", []):
        if dep["Path"] == dep_path_str:
            found_dep = True
            old_len = len(dep.get("On", []))
            dep["On"] = [h for h in dep.get("On", []) if h["Tool"] != dep_tool]
            new_len = len(dep["On"])
            if old_len != new_len:
                found_handler = True

            if not found_handler:
                U.log_warn(
                    f"No handler for tool '{dep_tool}' found on dependency",
                    f"path: {dep_path_str}",
                )
                sys.exit(0)

            U.save_json(tf, data)
            U.log_info(
                f"Removed handler from dependency in {tf.name}",
                f"path: {dep_path_str}, tool: {dep_tool}",
            )
            sys.exit(0)

    if not found_dep:
        U.log_warn(
            "Dependency not found",
            f"path: {dep_path_str}",
        )
        sys.exit(0)


if __name__ == "__main__":
    main()
