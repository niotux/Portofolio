"""empire target rm-dep <path>"""

import sys
from pathlib import Path

import target_utils as U


def main() -> None:
    if len(sys.argv) < 2:
        U.log_error("Usage: empire target rm-dep <path>")
        sys.exit(1)

    dep_path_str = sys.argv[1]
    data, tf = U.load_target()

    deps = data.get("Dependencies", [])
    filtered = [d for d in deps if d["Path"] != dep_path_str]

    if len(filtered) == len(deps):
        U.log_warn(
            "Dependency not found",
            f"path: {dep_path_str}, file: {tf.name}",
        )
        sys.exit(0)

    data["Dependencies"] = filtered
    U.save_json(tf, data)

    U.log_info(
        f"Removed dependency from {tf.name}",
        f"path: {dep_path_str}",
    )


if __name__ == "__main__":
    main()
