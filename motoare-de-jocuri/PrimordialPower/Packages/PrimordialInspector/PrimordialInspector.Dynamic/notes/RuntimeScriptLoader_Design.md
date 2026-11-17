# RuntimeScriptLoader Design Analysis & Solutions

## Current Problem

The current approach has two critical issues:

### 1. Constructor Mismatch Bug
- **Line 69** (cached): `Activator.CreateInstance(cachedType, instance)` - passes parameter
- **Line 85** (generated): `public {hostClassName}() {}` - parameterless constructor
- **Line 122** (first run): `Activator.CreateInstance(type)!` - no parameter

**Fix:** Make the constructor and instantiation consistent.

### 2. Inheritance Doesn't Achieve Goal
Even if the class inherits from the target type:
- The host instance has **different field values** than the inspected object
- Protected fields are accessible but contain **default values**, not runtime values
- Inheritance creates a **new instance**, not access to the existing one

## Proposed Solutions

### Option 1: Field Copying via Constructor (Recommended)
**Concept:** Generate a constructor that accepts the target instance and copies all accessible fields.

```csharp
public class ScriptHost_Game_1 : Game
{
    public ScriptHost_Game_1(Game source)
    {
        // Copy all public fields
        this.publicField1 = source.publicField1;
        this.publicField2 = source.publicField2;

        // Copy all protected fields via reflection
        var flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        foreach (var field in typeof(Game).GetFields(flags))
        {
            if (field.IsFamily || field.IsPublic) // protected or public
            {
                field.SetValue(this, field.GetValue(source));
            }
        }
    }

    public async Task Execute()
    {
        // Can now access fields directly without 'inspect.'
        // because they've been copied into this instance
        Console.WriteLine(publicField1);
    }
}
```

**Pros:**
- Direct field access in scripts (no `inspect.` prefix needed)
- Access to both public and protected fields
- True inheritance benefits

**Cons:**
- Modifications to fields don't affect the original object
- More complex constructor generation
- Performance overhead from field copying

---

### Option 2: Wrapper with Direct Field Access (Best Balance)
**Concept:** Don't inherit. Instead, generate properties that proxy to the target instance.

```csharp
public class ScriptHost_Game_1
{
    private Game _target;

    public ScriptHost_Game_1(Game target)
    {
        _target = target;
    }

    // Generate property proxies for each public/protected field
    public int publicField1
    {
        get => _target.publicField1;
        set => _target.publicField1 = value;
    }

    // For protected fields, use reflection
    public int protectedField1
    {
        get => (int)typeof(Game).GetField("protectedField1",
            BindingFlags.NonPublic | BindingFlags.Instance).GetValue(_target);
        set => typeof(Game).GetField("protectedField1",
            BindingFlags.NonPublic | BindingFlags.Instance).SetValue(_target, value);
    }

    public async Task Execute()
    {
        // Direct access, changes affect original object
        publicField1 = 42;
        protectedField1 = 100;
    }
}
```

**Pros:**
- Changes to fields affect the **original object**
- Direct field access (no prefix)
- Cleaner separation of concerns
- Can expose protected fields as public properties

**Cons:**
- More code generation complexity
- Slight reflection overhead for protected fields
- Can't directly override virtual methods

---

### Option 3: Simple Parameter Passing (Simplest)
**Concept:** Don't inherit, just pass the instance and require explicit access.

```csharp
public class ScriptHost_Game_1
{
    public ScriptHost_Game_1() { }

    public async Task Execute(Game inspect)
    {
        // User must explicitly reference 'inspect'
        inspect.PublicMethod();
        Console.WriteLine(inspect.publicField);

        // For protected fields, use reflection helpers
        var protectedValue = GetProtectedField<int>(inspect, "protectedField");
    }

    private T GetProtectedField<T>(object obj, string fieldName)
    {
        var field = obj.GetType().GetField(fieldName,
            BindingFlags.NonPublic | BindingFlags.Instance);
        return (T)field.GetValue(obj);
    }
}
```

**Pros:**
- Simple and straightforward
- Easy to understand and debug
- No complex code generation
- Changes affect original object

**Cons:**
- Requires `inspect.` prefix for all access
- Protected fields need helper methods
- Less "magic", more explicit

---

### Option 4: Dynamic Proxy with Field Injection (Most Flexible)
**Concept:** Use expression trees or source generators to create dynamic field access.

```csharp
public class ScriptHost_Game_1
{
    private Game _target;
    private Dictionary<string, (Func<object> getter, Action<object> setter)> _fields;

    public ScriptHost_Game_1(Game target)
    {
        _target = target;
        _fields = BuildFieldAccessors(target);
    }

    public dynamic this[string fieldName]
    {
        get => _fields[fieldName].getter();
        set => _fields[fieldName].setter(value);
    }

    public async Task Execute()
    {
        // Access via indexer or generated properties
        this["publicField1"] = 42;
    }
}
```

**Pros:**
- Maximum flexibility
- Can handle any field type
- Runtime field discovery

**Cons:**
- Complex implementation
- Less type-safe
- Performance overhead

---

## Recommendation: Option 2 (Wrapper with Property Proxies)

This provides the best balance of:
- Direct field access in scripts
- Changes affect the original object
- Type-safe at compile time
- Reasonable code generation complexity

### Implementation Strategy:
1. Reflect on target type to discover all public/protected fields
2. Generate wrapper class with property proxies
3. Use direct access for public fields, reflection for protected
4. Cache the generated type by (TargetType, FieldSignature)

### Quick Fix for Current Code:
Change **line 69** to:
```csharp
scriptInstance = Activator.CreateInstance(cachedType)!;
```

And change **line 125** to:
```csharp
return () => (Task)method.Invoke(scriptInstance, [instance])!;
```

This makes the cached path consistent with the first-run path.
