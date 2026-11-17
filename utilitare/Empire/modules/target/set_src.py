"""empire target set-src <path>"""

import sys

import target_utils as U


def main() -> None:
    if len(sys.argv) < 2:
        U.log_error("Usage: empire target set-src <path>")
        sys.exit(1)

    data, tf = U.load_target()
    old = data.get("Source", "")
    data["Source"] = sys.argv[1]
    U.save_json(tf, data)

    U.log_info("Set source", f"{old} -> {sys.argv[1]}")


if __name__ == "__main__":
    main()
