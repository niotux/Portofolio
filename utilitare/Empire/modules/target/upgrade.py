"""empire target upgrade — normalize target JSON to the latest schema."""

import target_utils as U


def main() -> None:
    data, tf = U.load_target()

    # Normalize to canonical schema
    upgraded = {
        "Name":         data.get("Name", ""),
        "Dependencies": [
            {
                "Path":   d.get("Path", ""),
                "Module": d.get("Module", ""),
                "On": [
                    {
                        "Tool":   h.get("Tool", ""),
                        "Option": h.get("Option", ""),
                    }
                    for h in d.get("On", [])
                ],
            }
            for d in data.get("Dependencies", [])
        ],
        "Source":     data.get("Source", ""),
        "OutputBase": data.get("OutputBase", ""),
        "Output":     data.get("Output", ""),
        "Artefacts":  data.get("Artefacts", ""),
        "Configs": [
            {
                "Name":  c.get("Name", ""),
                "Flags": c.get("Flags", ""),
            }
            for c in data.get("Configs", [])
        ],
    }

    U.save_json(tf, upgraded)
    U.log_info("Upgraded", tf.name)


if __name__ == "__main__":
    main()
