# RuntimeScriptLoader Approach & Process

## Overview
The RuntimeScriptLoader uses Roslyn to compile C# scripts at runtime and execute them with full access to target instance members through reflection-based helper methods.

## Core Architecture

### Components
1. **RuntimeScriptLoader** - Main compilation and caching logic
2. **ScriptContext\<T\>** - Base class providing reflection helpers for private member access
3. **Generated ScriptHost classes** - Dynamic classes inheriting from ScriptContext that execute user code
4. **Cache** - Thread-safe dictionary storing compiled types for reuse

## Process Flow

### 1. LoadScript Called
```
User → LoadScript(instance, code, usings)
```
- Validates input
- Extracts target type from instance
- Builds using declarations (implicit + user-provided)
- Calls CompileScript

### 2. Cache Lookup
```
code → SHA1 Hash → Cache Key: (TargetType, ScriptHash)
```
- Generate SHA1 hash of script code
- Check cache: `_cachedHosts[(targetType, scriptHash)]`
- **Cache Hit**: Reuse compiled type, skip compilation
- **Cache Miss**: Proceed to compilation

### 3. Code Generation (Cache Miss)
```csharp
using System;
using System.Collections.Generic;
// ... all using declarations ...

public class ScriptHost_TypeName_123 : ScriptContext<TargetType>
{
  public ScriptHost_TypeName_123(TargetType instance) : base(instance) { }

  public void Execute()
  {
    // User's script code injected here
  }
}
```

**Key aspects:**
- Unique class name per compilation: `ScriptHost_{TypeName}_{Counter}`
- Inherits from `ScriptContext<T>` to access reflection helpers
- Constructor receives target instance
- Execute method contains user code

### 4. Roslyn Compilation
```
Source Code → SyntaxTree → Compilation → Assembly (in-memory)
```
- Parse source to syntax tree
- Gather metadata references (target assembly, System assemblies, etc.)
- Create CSharpCompilation with references
- Emit to MemoryStream
- Load assembly from bytes

### 5. Type Extraction & Caching
```
Assembly → Extract ScriptHost class → Store in cache
```
- Get compiled type from assembly
- Store in `_cachedHosts[(targetType, scriptHash)] = type`
- Future identical scripts reuse this type

### 6. Instance Creation & Execution
```
Type → Create instance with target → Get Execute method → Return Action
```
- Create instance: `new ScriptHost(targetInstance)`
- Get Execute method via reflection
- Return `Action` delegate that invokes Execute

### 7. Script Execution
```
Action() → method.Invoke(scriptInstance, null) → Runs user code
```
- User calls the returned Action
- Invokes Execute method on script instance
- Script has access to:
  - `Instance` property (public members)
  - Helper methods: `GetField()`, `SetField()`, `InvokeMethod()`, etc.

## Caching Strategy

### Cache Key
```csharp
(Type TargetType, string ScriptHash)
```
- **TargetType**: Ensures different types don't share script hosts
- **ScriptHash**: SHA1 hash of script code

### Why This Works
- Same script + same type = same compiled class reused
- Different script = different hash = new compilation
- Different type = different cache entry = new compilation

### Performance Characteristics
- **First run**: Full compilation (~100-500ms depending on complexity)
- **Cached run**: Instance creation only (~1-5ms)
- **Memory**: One Type per unique (TargetType, ScriptHash) combination

### Thread Safety
- Uses `ConcurrentDictionary` for thread-safe cache access
- Multiple threads can safely load scripts simultaneously
- Cache checks and additions are atomic

## Known Limitations & Considerations

### 1. Using Statements Not in Cache Key
**Current Issue**: Hash only includes `code`, not `usings`

**Scenario**:
```csharp
// First call
LoadScript(obj, "var list = new List<int>();", ["System.Collections.Generic"]);

// Second call - same code, different usings
LoadScript(obj, "var list = new List<int>();", ["System.Collections"]);
```
Both would hit the same cache entry even though using statements differ.

**Impact**: Low (usings rarely change for same code)

**Fix**: Include usings in hash:
```csharp
var combined = string.Join("\n", usings) + "\n" + code;
var scriptHash = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(combined)));
```

### 2. Memory Growth
Each unique script creates a new Type that stays in memory. For long-running applications with many unique scripts, consider:
- Implementing cache eviction (LRU)
- Setting maximum cache size
- Periodically clearing cache

### 3. Assembly Unloading
Compiled assemblies cannot be unloaded (non-collectible). This is acceptable for most use cases but limits long-running applications with unlimited script variations.

**Potential Solution**: Use `AssemblyLoadContext` with collectible assemblies (more complex).

## Optimization Opportunities

### 1. Include Usings in Hash (Recommended)
```csharp
var fullSource = string.Join("\n", usings) + "\n" + code;
var scriptHash = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(fullSource)));
```

### 2. Metadata Reference Caching
Currently rebuilds reference list for each compilation. Could cache per TargetType:
```csharp
private static readonly ConcurrentDictionary<Type, List<MetadataReference>> _refCache = new();
```

### 3. Async Compilation
For non-blocking compilation in servers:
```csharp
public async Task<Action> LoadScriptAsync(object instance, string code, string[]? usings = null)
```

### 4. Pre-compilation
For known scripts, pre-compile at startup to avoid first-run penalty.

## Security Considerations

### Code Injection
Scripts can execute arbitrary C# code in the application's security context. Only accept scripts from trusted sources.

### Private Member Access
Scripts can access private members via reflection helpers. This is by design but should be restricted to trusted users.

### Resource Consumption
Malicious scripts could:
- Create infinite loops
- Allocate excessive memory
- Block threads

**Mitigations**:
- Run scripts with timeouts
- Implement cancellation tokens
- Sandbox execution context (advanced)

## Performance Metrics

### Compilation Time
- Simple script: ~100-200ms
- Complex script: ~300-500ms
- Cached script: ~1-5ms

### Memory Usage
- Per compiled type: ~10-50KB
- ScriptContext instance: ~1-2KB
- Total overhead: Minimal for typical use cases

## Future Enhancements

1. **Parameter Support**: Allow scripts to accept parameters
2. **Return Values**: Support `Func<T>` for scripts with return values
3. **Async Scripts**: Support `async/await` in scripts
4. **Debugging**: Source mapping for better error messages
5. **IntelliSense**: IDE support for script editing
