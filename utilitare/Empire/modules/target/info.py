"""empire target info"""

import target_utils as U


def main() -> None:
    data, _ = U.load_target()

    print()
    print(f"  Name:         {data.get('Name', '')}")
    print(f"  Source:       {data.get('Source', '') or '(none)'}")
    print(f"  OutputBase:   {data.get('OutputBase', '') or '(none)'}")
    print(f"  Output:       {data.get('Output', '') or '(none)'}")
    print(f"  Artefacts:    {data.get('Artefacts', '') or '(none)'}")
    print(f"  Dependencies: {len(data.get('Dependencies', []))}")
    print(f"  Configs:      {len(data.get('Configs', []))}")
    print()


if __name__ == "__main__":
    main()
