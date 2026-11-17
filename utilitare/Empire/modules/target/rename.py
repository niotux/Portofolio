"""empire target rename <new-name>"""

import sys

import target_utils as U


def main() -> None:
    if len(sys.argv) < 2:
        U.log_error("Usage: empire target rename <new-name>")
        sys.exit(1)

    new_name = sys.argv[1]
    data, tf = U.load_target()
    old_name = data.get("Name", "")

    data["Name"] = new_name
    U.save_json(tf, data)

    U.log_info(
        f"Renamed target in {tf.name}",
        f"{old_name} -> {new_name}",
    )


if __name__ == "__main__":
    main()
