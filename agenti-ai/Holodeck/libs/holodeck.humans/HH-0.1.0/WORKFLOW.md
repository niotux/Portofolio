# Retained baseline workflow

Use this complete source project for comparison and focused checks. Run `dotnet test Humans.slnx`. Its retained runtime is kept unchanged by `bash publish.sh`; if missing, rebuild from this clean checkout with `bash scripts/restore-version.sh`.

Keep new work in a direct sibling HH project. Never create another version inside this project. Source history and the original HH-0.1.0 tag remain intact. Actual challenge–test–note–improve development belongs to the HH-0.1.1 project.
