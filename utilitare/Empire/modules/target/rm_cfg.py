"""empire target rm-cfg <name>"""

import sys

import target_utils as U


def main() -> None:
    if len(sys.argv) < 2:
        U.log_error("Usage: empire target rm-cfg <name>")
        sys.exit(1)

    cfg_name = sys.argv[1]
    data, tf = U.load_target()

    cfgs = data.get("Configs", [])
    filtered = [c for c in cfgs if c["Name"] != cfg_name]

    if len(filtered) == len(cfgs):
        U.log_warn(
            "Config not found",
            f"name: {cfg_name}, file: {tf.name}",
        )
        sys.exit(0)

    data["Configs"] = filtered
    U.save_json(tf, data)

    U.log_info("Removed config", f"name: {cfg_name}")


if __name__ == "__main__":
    main()
