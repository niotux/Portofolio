"""empire target add-cfg <name> [flags]"""

import sys

import target_utils as U


def main() -> None:
    if len(sys.argv) < 2:
        U.log_error("Usage: empire target add-cfg <name> [flags]")
        sys.exit(1)

    cfg_name = sys.argv[1]
    cfg_flags = sys.argv[2] if len(sys.argv) > 2 else ""
    data, tf = U.load_target()

    if any(c["Name"] == cfg_name for c in data.get("Configs", [])):
        U.log_warn(
            "Config already exists",
            f"name: {cfg_name}, file: {tf.name}",
        )
        sys.exit(0)

    data.setdefault("Configs", []).append({"Name": cfg_name, "Flags": cfg_flags})
    U.save_json(tf, data)

    U.log_info("Added config", f"name: {cfg_name}")


if __name__ == "__main__":
    main()
