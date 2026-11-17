"""empire target set-on <path> [tool option ...]"""

import sys

import target_utils as U


def main() -> None:
    if len(sys.argv) < 2:
        U.log_error("Usage: empire target set-on <path> [tool option ...]")
        sys.exit(1)

    dep_path_str = sys.argv[1]
    pairs: list[dict] = []

    # Parse remaining args as [tool option] pairs
    args = sys.argv[2:]
    for i in range(0, len(args), 2):
        tool = args[i]
        option = args[i + 1] if i + 1 < len(args) else ""
        pairs.append({"Tool": tool, "Option": option})

    data, tf = U.load_target()
    found = False

    for dep in data.get("Dependencies", []):
        if dep["Path"] == dep_path_str:
            found = True
            # Remove existing entries for tools in pairs, then add new ones
            existing_tools = {h["Tool"] for h in dep.get("On", [])}
            new_pairs = [p for p in dep["On"]]
            for p in pairs:
                if p["Tool"] in existing_tools:
                    new_pairs = [h for h in new_pairs if h["Tool"] != p["Tool"]]
            new_pairs.extend(pairs)
            dep["On"] = new_pairs
            break

    if not found:
        U.log_warn(
            "Dependency not found",
            f"path: {dep_path_str}, file: {tf.name}",
        )
        sys.exit(0)

    U.save_json(tf, data)
    tool_names = " ".join(p["Tool"] for p in pairs)
    U.log_info(
        f"Set handlers on dependency in {tf.name}",
        f"path: {dep_path_str}, tools: {tool_names}",
    )


if __name__ == "__main__":
    main()
