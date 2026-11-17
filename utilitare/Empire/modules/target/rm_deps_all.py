"""empire target rm-deps-all"""

import sys
import target_utils as U


def main() -> None:
    data, tf = U.load_target()
    count = len(data.get("Dependencies", []))

    if count == 0:
        U.log_warn(
            "No dependencies to remove",
            f"file: {tf.name}",
        )
        sys.exit(0)

    data["Dependencies"] = []
    U.save_json(tf, data)

    U.log_info(
        f"Removed all dependencies from {tf.name}",
        f"removed {count}",
    )


if __name__ == "__main__":
    main()
