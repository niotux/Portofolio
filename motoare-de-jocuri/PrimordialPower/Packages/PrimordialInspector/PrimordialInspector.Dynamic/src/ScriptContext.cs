using System.Reflection;

namespace PrimordialInspector.Scripting;

/// <summary>
/// Base class for script execution contexts that provides reflection-based access to private members.
/// Scripts inherit from this class to gain access to helper methods for private member access.
/// </summary>
/// <typeparam name="T">The type of the target instance</typeparam>
public abstract class ScriptContext<T> where T : class
{
  private readonly T _instance;
  private static readonly BindingFlags PrivateInstanceFlags =
    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
  private static readonly BindingFlags PrivateStaticFlags =
    BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;

  protected ScriptContext(T instance)
  {
    _instance = instance ?? throw new ArgumentNullException(nameof(instance));
  }

  /// <summary>
  /// Gets the value of a private or public field
  /// </summary>
  protected TField GetField<TField>(string fieldName)
  {
    var field = typeof(T).GetField(fieldName, PrivateInstanceFlags);
    if (field == null)
      throw new MissingFieldException($"Field '{fieldName}' not found on type '{typeof(T).Name}'");

    var value = field.GetValue(_instance);
    return value is TField typedValue ? typedValue : default!;
  }

  /// <summary>
  /// Sets the value of a private or public field
  /// </summary>
  protected void SetField<TField>(string fieldName, TField value)
  {
    var field = typeof(T).GetField(fieldName, PrivateInstanceFlags);
    if (field == null)
      throw new MissingFieldException($"Field '{fieldName}' not found on type '{typeof(T).Name}'");

    field.SetValue(_instance, value);
  }

  /// <summary>
  /// Gets the value of a private or public property
  /// </summary>
  protected TProperty GetProperty<TProperty>(string propertyName)
  {
    var property = typeof(T).GetProperty(propertyName, PrivateInstanceFlags);
    if (property == null)
      throw new MissingMemberException($"Property '{propertyName}' not found on type '{typeof(T).Name}'");

    var value = property.GetValue(_instance);
    return value is TProperty typedValue ? typedValue : default!;
  }

  /// <summary>
  /// Sets the value of a private or public property
  /// </summary>
  protected void SetProperty<TProperty>(string propertyName, TProperty value)
  {
    var property = typeof(T).GetProperty(propertyName, PrivateInstanceFlags);
    if (property == null)
      throw new MissingMemberException($"Property '{propertyName}' not found on type '{typeof(T).Name}'");

    property.SetValue(_instance, value);
  }

  /// <summary>
  /// Invokes a private or public instance method
  /// </summary>
  protected TResult InvokeMethod<TResult>(string methodName, params object[] parameters)
  {
    var method = typeof(T).GetMethod(methodName, PrivateInstanceFlags);
    if (method == null)
      throw new MissingMethodException($"Method '{methodName}' not found on type '{typeof(T).Name}'");

    var result = method.Invoke(_instance, parameters);
    return result is TResult typedResult ? typedResult : default!;
  }

  /// <summary>
  /// Invokes a private or public instance method that returns void
  /// </summary>
  protected void InvokeMethod(string methodName, params object[] parameters)
  {
    var method = typeof(T).GetMethod(methodName, PrivateInstanceFlags);
    if (method == null)
      throw new MissingMethodException($"Method '{methodName}' not found on type '{typeof(T).Name}'");

    method.Invoke(_instance, parameters);
  }

  /// <summary>
  /// Gets the value of a private or public static field
  /// </summary>
  protected static TField GetStaticField<TField>(string fieldName)
  {
    var field = typeof(T).GetField(fieldName, PrivateStaticFlags);
    if (field == null)
      throw new MissingFieldException($"Static field '{fieldName}' not found on type '{typeof(T).Name}'");

    var value = field.GetValue(null);
    return value is TField typedValue ? typedValue : default!;
  }

  /// <summary>
  /// Sets the value of a private or public static field
  /// </summary>
  protected static void SetStaticField<TField>(string fieldName, TField value)
  {
    var field = typeof(T).GetField(fieldName, PrivateStaticFlags);
    if (field == null)
      throw new MissingFieldException($"Static field '{fieldName}' not found on type '{typeof(T).Name}'");

    field.SetValue(null, value);
  }

  /// <summary>
  /// Gets the value of a private or public static property
  /// </summary>
  protected static TProperty GetStaticProperty<TProperty>(string propertyName)
  {
    var property = typeof(T).GetProperty(propertyName, PrivateStaticFlags);
    if (property == null)
      throw new MissingMemberException($"Static property '{propertyName}' not found on type '{typeof(T).Name}'");

    var value = property.GetValue(null);
    return value is TProperty typedValue ? typedValue : default!;
  }

  /// <summary>
  /// Sets the value of a private or public static property
  /// </summary>
  protected static void SetStaticProperty<TProperty>(string propertyName, TProperty value)
  {
    var property = typeof(T).GetProperty(propertyName, PrivateStaticFlags);
    if (property == null)
      throw new MissingMemberException($"Static property '{propertyName}' not found on type '{typeof(T).Name}'");

    property.SetValue(null, value);
  }

  /// <summary>
  /// Invokes a private or public static method
  /// </summary>
  protected static TResult InvokeStaticMethod<TResult>(string methodName, params object[] parameters)
  {
    var method = typeof(T).GetMethod(methodName, PrivateStaticFlags);
    if (method == null)
      throw new MissingMethodException($"Static method '{methodName}' not found on type '{typeof(T).Name}'");

    var result = method.Invoke(null, parameters);
    return result is TResult typedResult ? typedResult : default!;
  }

  /// <summary>
  /// Invokes a private or public static method that returns void
  /// </summary>
  protected static void InvokeStaticMethod(string methodName, params object[] parameters)
  {
    var method = typeof(T).GetMethod(methodName, PrivateStaticFlags);
    if (method == null)
      throw new MissingMethodException($"Static method '{methodName}' not found on type '{typeof(T).Name}'");

    method.Invoke(null, parameters);
  }

  /// <summary>
  /// Gets direct access to the target instance for public member access
  /// </summary>
  protected T Instance => _instance;
}
