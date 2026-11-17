"""empire target get-src"""

import target_utils as U


def main() -> None:
    data, _ = U.load_target()
    val = data.get("Source", "")
    U.log_info("Source", val)


if __name__ == "__main__":
    main()
