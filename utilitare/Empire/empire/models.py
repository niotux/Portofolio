"""Domain dataclasses for modules, tools, targets, dependencies, etc."""

from __future__ import annotations

from dataclasses import dataclass, field


# ── Option ────────────────────────────────────────────────────────────────────

@dataclass
class Option:
    name:           str
    flags:          str          = ""
    linking:        str | None   = None
    propagate:      bool         = False
    copy:           bool         = False
    require_config: bool         = False
    create_dirs:    bool         = False

    @staticmethod
    def from_dict(d: dict) -> "Option":
        return Option(
            name           = d["Name"],
            flags          = d.get("Flags", ""),
            linking        = d.get("Linking"),
            propagate      = d.get("Propagate", False),
            copy           = d.get("Copy", False),
            require_config = d.get("RequireConfig", False),
            create_dirs    = d.get("CreateDirs", False),
        )


# ── Tool ──────────────────────────────────────────────────────────────────────

@dataclass
class Tool:
    name:           str
    command:        str               = ""
    help_args:      list              = field(default_factory=list)
    propagate:      bool              = False
    require_config: bool              = False
    create_dirs:    bool              = False
    options:        list[Option]      = field(default_factory=list)
    _options:       dict[str, Option] = field(default_factory=dict, repr=False, compare=False)

    @staticmethod
    def from_dict(d: dict) -> "Tool":
        options = [Option.from_dict(o) for o in d.get("Options", [])]
        help_args = d.get("Help", [])
        t = Tool(
            name           = d["Name"],
            command        = d.get("Command", ""),
            help_args      = help_args,
            propagate      = d.get("Propagate", False),
            require_config = d.get("RequireConfig", False),
            create_dirs    = d.get("CreateDirs", False),
            options        = options,
        )
        t._options = {o.name: o for o in options}
        return t

    def option(self, name: str) -> Option | None:
        return self._options.get(name)


# ── Config ────────────────────────────────────────────────────────────────────

@dataclass
class Config:
    name:  str
    flags: str = ""

    @staticmethod
    def from_dict(d: dict) -> "Config":
        return Config(name=d["Name"], flags=d.get("Flags", ""))


# ── Template ──────────────────────────────────────────────────────────────────

@dataclass
class Template:
    name:         str
    dependencies: list         = field(default_factory=list)
    source:       str          = ""
    output_base:  str          = ""
    output:       str          = ""
    artefacts:    str          = ""
    configs:      list[Config] = field(default_factory=list)

    @staticmethod
    def from_dict(d: dict) -> "Template":
        return Template(
            name         = d["Name"],
            dependencies = d.get("Dependencies", []),
            source       = d.get("Source", ""),
            output_base  = d.get("OutputBase", ""),
            output       = d.get("Output", ""),
            artefacts    = d.get("Artefacts", ""),
            configs      = [Config.from_dict(c) for c in d.get("Configs", [])],
        )


# ── Module ─────────────────────────────────────────────────────────────────────

@dataclass
class Module:
    name:       str
    type:       str                  = "target"
    tools:      list[Tool]           = field(default_factory=list)
    templates:  list[Template]       = field(default_factory=list)
    _tools:     dict[str, Tool]      = field(default_factory=dict, repr=False, compare=False)
    _templates: dict[str, Template]  = field(default_factory=dict, repr=False, compare=False)

    @staticmethod
    def from_dict(name: str, d: dict) -> "Module":
        tools     = [Tool.from_dict(t)     for t in d.get("Tools",     [])]
        templates = [Template.from_dict(t) for t in d.get("Templates", [])]
        m = Module(
            name      = name,
            type      = d.get("Type", "target"),
            tools     = tools,
            templates = templates,
        )
        m._tools     = {t.name: t for t in tools}
        m._templates = {t.name: t for t in templates}
        return m

    def tool(self, name: str) -> Tool | None:
        return self._tools.get(name)

    def template(self, name: str) -> Template | None:
        return self._templates.get(name)

    def option_anywhere(self, name: str) -> Option | None:
        for t in self.tools:
            o = t.option(name)
            if o:
                return o
        return None


# ── DepHandler ────────────────────────────────────────────────────────────────

@dataclass
class DepHandler:
    tool:   str
    option: str

    @staticmethod
    def from_dict(d: dict) -> "DepHandler":
        return DepHandler(tool=d["Tool"], option=d["Option"])


# ── Dep ────────────────────────────────────────────────────────────────────────

@dataclass
class Dep:
    path:   str
    module: str
    on:     list[DepHandler] = field(default_factory=list)

    @staticmethod
    def from_dict(d: dict) -> "Dep":
        return Dep(
            path   = d["Path"],
            module = d.get("Module", ""),
            on     = [DepHandler.from_dict(h) for h in d.get("On", [])],
        )


# ── Target ─────────────────────────────────────────────────────────────────────

@dataclass
class Target:
    name:         str
    dependencies: list[Dep]    = field(default_factory=list)
    source:       str          = ""
    output_base:  str          = ""
    output:       str          = ""
    artefacts:    str          = ""
    configs:      list[Config] = field(default_factory=list)
    _configs:     dict[str, Config] = field(default_factory=dict, repr=False, compare=False)

    @staticmethod
    def from_dict(d: dict) -> "Target":
        configs = [Config.from_dict(c) for c in d.get("Configs", [])]
        t = Target(
            name         = d["Name"],
            dependencies = [Dep.from_dict(dep) for dep in d.get("Dependencies", [])],
            source       = d.get("Source", ""),
            output_base  = d.get("OutputBase", ""),
            output       = d.get("Output", ""),
            artefacts    = d.get("Artefacts", ""),
            configs      = configs,
        )
        t._configs = {c.name: c for c in configs}
        return t

    def config(self, name: str) -> Config | None:
        return self._configs.get(name)

    def to_dict(self) -> dict:
        return {
            "Name":         self.name,
            "Dependencies": [
                {
                    "Path":   dep.path,
                    "Module": dep.module,
                    "On":     [{"Tool": h.tool, "Option": h.option} for h in dep.on],
                }
                for dep in self.dependencies
            ],
            "Source":       self.source,
            "OutputBase":   self.output_base,
            "Output":       self.output,
            "Artefacts":    self.artefacts,
            "Configs":      [{"Name": c.name, "Flags": c.flags} for c in self.configs],
        }
