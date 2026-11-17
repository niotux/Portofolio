# QuantiX.Engine.Renderers.Quartz — Rules

- Follow and implement the interfaces from `QuantiX.Engine.Rendering` exactly.
- Do NOT expose OpenGL or platform-specific logic in public API.
- Do NOT leak Silk.NET.OpenGL types or any binding library API.
- Do NOT expose any API other than what the rendering interfaces define.
- If something feels weird or unnatural to implement, or involves strange workarounds, flag it immediately — do not silently work around it.
