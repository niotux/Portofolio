"""empire target get-outbase"""

import target_utils as U


def main() -> None:
    data, _ = U.load_target()
    val = data.get("OutputBase", "")
    U.log_info("OutputBase", val)


if __name__ == "__main__":
    main()
