"""empire target add-dep <path>"""

import sys
from pathlib import Path

import target_utils as U


def main() -> None:
    if len(sys.argv) < 2:
        log_error("Usage: empire target add-dep <path>")
        sys.exit(1)
    dep_path_str = sys.argv[1]
    dep_path = Path(dep_path_str)

    if not dep_path.is_dir():
        U.log_error("Directory does not exist", f"path: {dep_path_str}")
        sys.exit(1)

    data, tf = U.load_target()
    dep_module = U.detect_module(dep_path_str)

    # Check for duplicates
    if any(d["Path"] == dep_path_str for d in data.get("Dependencies", [])):
        U.log_warn(
            "Dependency already exists",
            f"path: {dep_path_str}, file: {tf.name}",
        )
        sys.exit(0)

    dep = {"Path": dep_path_str, "Module": dep_module, "On": []}
    data.setdefault("Dependencies", []).append(dep)
    U.save_json(tf, data)

    U.log_info(
        f"Added dependency to {tf.name}",
        f"path: {dep_path_str}, module: {dep_module}",
    )


if __name__ == "__main__":
    main()
