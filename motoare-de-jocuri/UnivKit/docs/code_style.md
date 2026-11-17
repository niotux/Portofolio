# UnivKit Code Style

This document instructs AI assistants and contributors how to format and
structure C# code in UnivKit. Every rule below is mandatory unless an
explicit exception is stated.

## 1. Formatting

### Indentation

**Two spaces per indent level.** No tabs.

### Braces and blocks

Use Allman style everywhere — for braces, brackets, and parentheses. The
opening brace always goes on its own line.

Always use `{}` on `if`, `else`, `while`, `for`, `foreach`, `using`,
`lock`, `switch`, and every other control-flow construct. Never write a
braceless single-line body.

```csharp
if (ready)
{
  this.Start();
}
```

### Method bodies

Expression-bodied `=>` methods are allowed only when the body is
trivially obvious (a simple forward or one-line computation). Anything
with branching or multiple operations uses explicit `{}`.

### Method calls with many parameters

When a call would be long or has many arguments, break it Allman-style:
the opening paren stays on the call line, each argument on its own line,
closing paren aligned with the call site.

```csharp
this.Render
(
  target,
  bounds,
  palette,
  flags
);
```

### Namespaces and usings

- Always use **file-scoped** namespaces.
- Place all `using` directives at the top of the file, above the
  namespace declaration.

```csharp
using System;
using System.Collections.Generic;

namespace UnivKit.Example;

public sealed class Thing
{
}
```

### Regions and comments

- Never use `#region` / `#endregion`.
- Do **not** write comments, with one exception: implementation
  comments inside a method body may be used to explain *non-obvious*
  logic. If a reader can derive the intent from the code, no comment.
- XML doc comments (`/// <summary>`...) are **not yet used** in this
  project. Do not add them until the documentation conventions are
  defined.

## 2. Naming conventions

| Kind                                            | Convention     |
|-------------------------------------------------|----------------|
| Types, methods, public properties, public fields, constants, tuple members | `PascalCase`   |
| Private / protected **instance** fields, local variables, parameters       | `camelCase`    |
| Private **static** fields                                                  | `_camelCase`   |

Rationale for `_camelCase` on private static fields: no `this` is
available to disambiguate, so the leading underscore prevents name
collisions with locals and parameters.

### `this.` prefix

Always prefix `this.` when accessing private fields *and* public
properties from inside the class. This is a readability requirement,
not an optional preference.

```csharp
public void Resize(int width, int height)
{
  this.width = width;
  this.height = height;
  this.Invalidate();
}
```

**Exception:** math-heavy single-line expressions where `this.` would
bloat the formula. Omit `this.` in that case.

```csharp
public float LengthSquared => X * X + Y * Y + Z * Z;
```

### Protected fields

Avoid `protected` fields on classes designed to be inherited from
**outside** the project. Downstream consumers may use a different
convention (e.g. `_camelCase` for instance fields) and exposing
`camelCase` protected fields would clash with their style.

Use protected fields only on **internal** base classes where all
subclasses live inside this project. Never expose them publicly in any
form.

## 3. Property formatting

Prefer concise forms when the accessor body is obvious. Escalate to
`{}` only when the logic actually warrants it.

**Getter-only, simple** — use expression body on the property:

```csharp
public int Count => this.items.Count;
```

**Getter and setter, both simple** — use `=>` on each accessor inside
braces, one accessor per line:

```csharp
public int Width
{
  get => this.width;
  set => this.width = value;
}
```

**Any accessor is more involved** — that specific accessor uses `{}`;
the other may stay on `=>` if it is still simple:

```csharp
public int Width
{
  get => this.width;
  set
  {
    this.width = value;
    this.InvalidateLayout();
  }
}
```

Never write a braced accessor that contains only a single `return` —
collapse it to `=>`.

## 4. Inheritance declaration

Threshold-based, not single-vs-multiple:

- **Up to ~5 bases/interfaces total** — write the `:` on the same line
  as the class name.
- **More than that** — put the `:` on the next line and place each
  interface on its own line (column style).

```csharp
public sealed class Button : Control, IClickable
{
}
```

```csharp
public sealed class SuperiorClass
: IActivity,
  IAssetProvider,
  IAudio,
  IAudioPlayer,
  IRenderable,
  IDisposable,
  ISerializable
{
}
```

## 5. File composition

- **One public type per file** — strict.
- Small, tightly-related private/internal helper types *may* share a
  file with the public type they support. Example: an event-handler
  public type that needs several small internal record/enum helpers
  used only to describe its events. Public stand-alone types always
  live in their own file.

## 6. Intra-class ordering

Order members from **class-wide / abstract** at the top down to
**localized / precise** at the bottom. Group by *functional relation*,
not by access modifier or static/instance. The flow is:

1. **Constants** — class-level constants first.
2. **Core infrastructure** — private fields fundamental to the class
   (owned resources, disposables, native handles), followed by
   constructors, finalizer (if any), and `Dispose` / cleanup methods.
3. **State / property groups** — one group per logical property:
   private backing field(s) + the public property + any helpers tied
   specifically to that property. Group by relation.
4. **Behavior groups** — one group per logical behavior: any
   behavior-local private fields + the public method(s) + the private
   helpers used by those methods. Keep helpers immediately after the
   public method they serve.
5. **Generic helpers at the bottom** — error-handling helpers, static
   utilities, and nested helper types that are not tied to a specific
   property or behavior group.

### Spacing

- **Between a backing field and its property** — no gap; keep them stuck together.
- **Between property groups** (different logical properties) — one empty line.
- **Between methods in the same group** — one empty line.
- **Between major sections** (e.g., "Core infrastructure" to "State / property groups") — three empty lines.

### Sketch

```csharp
public sealed class Example : IDisposable
{
  // 1. Constants
  private const int DefaultCapacity = 16;



  // 2. Core infrastructure
  private readonly IntPtr handle;
  private bool disposed;

  public Example(IntPtr handle)
  {
    this.handle = handle;
  }

  ~Example()
  {
    this.Dispose(false);
  }

  public void Dispose()
  {
    this.Dispose(true);
    GC.SuppressFinalize(this);
  }

  private void Dispose(bool disposing)
  {
    if (this.disposed)
    {
      return;
    }
    this.disposed = true;
  }



  // 3. State / property group: Width
  private int width;
  public int Width
  {
    get => this.width;
    set
    {
      this.width = value;
      this.InvalidateLayout();
    }
  }

  private void InvalidateLayout()
  {
  }

  // State / property group: Height
  private int height;
  public int Height
  {
    get => this.height;
    set
    {
      this.height = value;
      this.InvalidateLayout();
    }
  }



  // 4. Behavior group: Render
  private int renderFrame;

  public void Render()
  {
    this.renderFrame++;
    this.DrawBackground();
    this.DrawForeground();
  }

  private void DrawBackground()
  {
  }

  private void DrawForeground()
  {
  }

  // Behavior group: Update
  public void Update()
  {
    this.Render();
  }



  // 5. Generic helpers
  private static void ThrowIfDisposed(bool disposed)
  {
    if (disposed)
    {
      throw new ObjectDisposedException(nameof(Example));
    }
  }
}
```
