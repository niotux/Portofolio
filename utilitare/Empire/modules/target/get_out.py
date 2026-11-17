"""empire target get-out"""

import target_utils as U


def main() -> None:
    data, _ = U.load_target()
    val = data.get("Output", "")
    U.log_info("Output", val)


if __name__ == "__main__":
    main()
