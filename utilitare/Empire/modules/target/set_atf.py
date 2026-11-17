"""empire target set-atf <path>"""

import sys

import target_utils as U


def main() -> None:
    if len(sys.argv) < 2:
        U.log_error("Usage: empire target set-atf <path>")
        sys.exit(1)

    data, tf = U.load_target()
    old = data.get("Artefacts", "")
    data["Artefacts"] = sys.argv[1]
    U.save_json(tf, data)

    U.log_info("Set artefacts", f"{old} -> {sys.argv[1]}")


if __name__ == "__main__":
    main()
