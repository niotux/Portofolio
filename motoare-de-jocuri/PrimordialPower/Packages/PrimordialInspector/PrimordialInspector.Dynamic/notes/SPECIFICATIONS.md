# RuntimeScriptLoader Specifications

## Overview
A reflection-based runtime scripting tool that allows dynamic method injection into any class, providing full access to all members (public, private, protected) as if the script were written inside the class itself.

## Core Concept
From the scripter's point of view, they are writing code **inside** a method of the target class. The script behaves as if it's a lambda expression defined within that class.

## Script Perspective

### Signature
The scripter writes code as if creating a lambda with the signature:
```csharp
Action scriptLambda = () =>
{
    // Script code here
};
```

- **Parameters**: None
- **Return Type**: `void`
- **Execution Context**: Inside the target class/type

### Access Scope
When a script is loaded from a specific type's context (e.g., from `Program.Main()`), the scripter has access to:

- **All instance fields** (public, private, protected)
- **All static fields** (public, private, protected)
- **All instance properties** (public, private, protected)
- **All static properties** (public, private, protected)
- **All instance methods** (public, private, protected)
- **All static methods** (public, private, protected)
- **All using statements** from the source file where the class is defined

### Example Usage

#### Target Class (Program.cs)
```csharp
using System;
using System.Collections.Generic;
using System.Linq;

namespace MyApp;

class Program
{
    private static int _counter = 0;
    private string _name = "Test";
    public int PublicValue = 42;

    private void IncrementCounter()
    {
        _counter++;
    }

    static void Main()
    {
        var program = new Program();
        var loader = new RuntimeScriptLoader();

        // Load script in context of 'program' instance
        var script = loader.LoadScript(program, @"
            // Access PUBLIC members directly through Instance property
            Console.WriteLine(Instance.PublicValue);
            Instance.PublicValue = 100;

            // Access PRIVATE members using helper methods
            var name = GetField<string>(""_name"");
            Console.WriteLine(name);

            SetField(""_name"", ""NewName"");

            InvokeMethod(""IncrementCounter"");

            var counter = GetStaticField<int>(""_counter"");
            SetStaticField(""_counter"", counter + 10);

            // All using statements from Program.cs are available
            var list = new List<int> { 1, 2, 3 };
            var sum = list.Sum();  // LINQ available via 'using System.Linq;'
        ", sourceFilePath: ""Program.cs"");

        script(); // Execute the script
    }
}
```

#### Available Helper Methods

The script inherits from `ScriptContext<T>` which provides these protected methods:

**Instance Members:**
- `T Instance` - Direct access to the target instance for public members
- `TField GetField<TField>(string fieldName)` - Get private/public field value
- `void SetField<TField>(string fieldName, TField value)` - Set private/public field value
- `TProperty GetProperty<TProperty>(string propertyName)` - Get private/public property value
- `void SetProperty<TProperty>(string propertyName, TProperty value)` - Set private/public property value
- `TResult InvokeMethod<TResult>(string methodName, params object[] parameters)` - Call private/public method with return value
- `void InvokeMethod(string methodName, params object[] parameters)` - Call private/public void method

**Static Members:**
- `TField GetStaticField<TField>(string fieldName)` - Get private/public static field value
- `void SetStaticField<TField>(string fieldName, TField value)` - Set private/public static field value
- `TProperty GetStaticProperty<TProperty>(string propertyName)` - Get private/public static property value
- `void SetStaticProperty<TProperty>(string propertyName, TProperty value)` - Set private/public static property value
- `TResult InvokeStaticMethod<TResult>(string methodName, params object[] parameters)` - Call private/public static method with return value
- `void InvokeStaticMethod(string methodName, params object[] parameters)` - Call private/public static void method

## Technical Requirements

### Must Support
1. **Private Member Access**: Full access to private/protected members without compile-time restrictions
2. **Using Statement Injection**: Automatically extract and include all `using` statements from the target class's source file
3. **Instance Context**: Script executes with `this` bound to the target instance
4. **Static Member Access**: Access to static members of the class
5. **Type Safety**: Runtime compilation with type checking

### Implementation Strategy
- Use Roslyn API to parse source files and extract using statements
- Generate a class that inherits from `ScriptContext<T>` base class
- Compile scripts as instance methods within the generated class
- Use reflection-based helper methods from `ScriptContext<T>` to enable private member access
- Public members can be accessed directly through the `Instance` property
- The script is compiled with full type-safety and IntelliSense-compatible syntax

## API Design

### Primary Method
```csharp
public Action LoadScript(object targetInstance, string scriptCode, string? sourceFilePath = null)
```

**Parameters**:
- `targetInstance`: The instance whose context the script executes in
- `scriptCode`: The C# code to execute (written as method body)
- `sourceFilePath`: Optional path to source file for extracting using statements (auto-detected if possible)

**Returns**: `Action` delegate that executes the script

**Throws**:
- `ArgumentNullException`: If targetInstance or scriptCode is null
- `CompilationException`: If script has syntax/compilation errors
- `FileNotFoundException`: If source file path is invalid

## Future Enhancements
- Support for generic return types: `LoadScript<T>(...)` returning `Func<T>`
- Support for parameterized scripts: `LoadScript<T1, T2>(...)` accepting parameters
- Caching compiled scripts for performance
- Hot-reload support for script modifications
- Debugging support with source mapping
