# HH-0.1.0

The retained baseline Human project. It contains its own `src/`, `tests/`, prompt files, configuration, blueprint and retained executable. HH-0.1.1 is a direct sibling development project, not contained here.

The application source is unchanged from the original HH-0.1.0 tag. This checkout has a layout-only correction removing the old nested release packaging. The original tag and history remain available. There is no shared database prompt registry in this baseline; that work belongs to HH-0.1.1.

```bash
dotnet test Humans.slnx
bash publish.sh                   # keeps the installed retained executable unchanged
bash scripts/restore-version.sh   # rebuild only if the retained executable is missing
```

See [BLUEPRINT.md](BLUEPRINT.md) for actual behaviour. New implementation work belongs in the next sibling project. The outer library keeps the original runtime/prompt paths for existing people.
