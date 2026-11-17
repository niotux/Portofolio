"""empire target get-atf"""

import target_utils as U


def main() -> None:
    data, _ = U.load_target()
    val = data.get("Artefacts", "")
    U.log_info("Artefacts", val)


if __name__ == "__main__":
    main()
