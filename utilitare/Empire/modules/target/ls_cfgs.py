"""empire target ls-cfgs"""

import sys

import target_utils as U


def main() -> None:
    data, _ = U.load_target()
    cfgs = data.get("Configs", [])

    if not cfgs:
        U.log_info("No configs", "file")
        sys.exit(0)

    print()
    print(f"  Configs ({len(cfgs)}):")

    for cfg in cfgs:
        print(f"    {cfg['Name']}  {cfg.get('Flags', '')}")

    print()


if __name__ == "__main__":
    main()
