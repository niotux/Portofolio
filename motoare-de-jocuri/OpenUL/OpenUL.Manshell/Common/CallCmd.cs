using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using VB = Microsoft.VisualBasic;

namespace OpenUL.Manshell.Common;

public sealed class CallCmd : ManshellCmd
{
  public override Type[] Args { get; } =
  [
    typeof(object),
    typeof(ManshellNamedArg),
    typeof(object[]),
    typeof(object[]),
  ];
  public override int MinimumArgs => 2;
  public override string[] Names { get; } = ["call"];

  protected override object? OnRun(ManshellContext context, ReadOnlySpan<ManshellArg> args)
  {
    // Self
    var selfObj = context.GetValue(args[0]);
    if (selfObj == null)
    {
      context.Throw(new NullReferenceException());
      return null;
    }

    // MethodName
    var methodName = args[1].Name;

    // == 2
    if (args.Length == 2)
    {
      var res = CallMethod(methodName, selfObj, null, null, null);
      if (res == CallError.Instance)
      {
        context.Throw(GetAccessException(selfObj.GetType(), methodName));
        return null;
      }
      return res;
    }

    // == 3
    if (args.Length == 3)
    {
      var paramsObjs = args[2].Value as object?[] ?? throw new ManshellCmdArgException(this, 2, "Expected an object array");
      var paramsTyes = Array.ConvertAll(paramsObjs, (o) => o?.GetType() ?? typeof(object));
      var res = CallMethod(methodName, selfObj, null, paramsTyes, paramsObjs);
      if (res == CallError.Instance)
      {
        context.Throw(GetAccessException(selfObj.GetType(), methodName));
        return null;
      }
      return res;
    }

    // >= 4
    {
      var genericArgsObjs = args[2].Value as object?[] ?? throw new ManshellCmdArgException(this, 2, "Expected a System.Type array");

      var genericArgs = Array.ConvertAll(genericArgsObjs, o => o as Type ?? throw new ManshellCmdArgException(this, 2, "Expected a System.Type array"));

      var paramsObjs = args[3].Value as object?[] ?? throw new ManshellCmdArgException(this, 3, "Expected an object array");
      var paramsTypes = Array.ConvertAll(paramsObjs, (o) => o?.GetType() ?? typeof(object));

      var resultObj = CallMethod(methodName, selfObj, genericArgs, paramsTypes, paramsObjs);

      return resultObj;
    }
  }

  private static MemberAccessException GetAccessException(Type type, string methodName)
    => new($"'{methodName}' is not a valid method on type '{type}'");

  private static object? CallMethod(string methodName, object selfObj, Type[]? genericTypes, Type[]? paramsTypes, object?[]? paramsObjs)
  {
    Type type = selfObj as Type ?? selfObj.GetType();

    MethodInfo? methInfo;
    if (genericTypes == null || genericTypes.Length == 0)
    {
      methInfo = FindMethod(type, methodName, paramsTypes ?? []);
    }
    else
    {
      methInfo = ResolveGenericMethod(type, methodName, genericTypes, paramsTypes ?? []);
    }

    if (methInfo != null)
    {
      var resultObj = methInfo.Invoke((object)type == selfObj && methInfo.IsStatic ? null : selfObj, paramsObjs);
      return resultObj;
    }
    return CallError.Instance;
  }
  private class CallError
  {
    private CallError()
    {

    }

    public static CallError Instance { get; } = new();
  }

  private static MethodInfo? ResolveGenericMethod(Type type, string methodName, Type[] typeArgs, Type[] typeParams)
  {
    foreach (var candidate in type.GetMethods())
    {
      if (candidate.Name != methodName || !candidate.IsGenericMethodDefinition)
      {
        continue;
      }
      if (candidate.GetGenericArguments().Length != typeArgs.Length)
      {
        continue;
      }

      MethodInfo constructed;
      try
      {
        constructed = candidate.MakeGenericMethod(typeArgs);
      }
      catch (ArgumentException)
      {
        continue;
      }

      var parameters = constructed.GetParameters();
      if (parameters.Length != typeParams.Length)
      {
        continue;
      }

      bool match = true;
      for (int i = 0; i < parameters.Length; i++)
      {
        if (parameters[i].ParameterType != typeParams[i])
        {
          match = false;
          break;
        }
      }

      if (match)
      {
        return constructed;
      }
    }

    return null;
  }

  private static MethodInfo? FindMethod(Type type, string methodName, Type[] parameterTypes)
  {
    // First try ordinary/non-generic methods.
    var method = type.GetMethod(methodName, parameterTypes);

    if (method != null)
      return method;

    // Then try generic methods with inferred type arguments.
    foreach (var candidate in type.GetMethods())
    {
      if (candidate.Name != methodName || !candidate.IsGenericMethodDefinition)
      {
        continue;
      }

      var parameters = candidate.GetParameters();

      if (parameters.Length != parameterTypes.Length)
        continue;

      var genericArgs = candidate.GetGenericArguments();

      if (genericArgs.Length != parameters.Length)
        continue;

      var inferredTypes = new Type[genericArgs.Length];
      var valid = true;

      for (var i = 0; i < parameters.Length; i++)
      {
        var parameterType = parameters[i].ParameterType;

        // Simple case: T1, T2, ...
        if (parameterType.IsGenericParameter)
        {
          inferredTypes[parameterType.GenericParameterPosition] = parameterTypes[i];
        }
        else
        {
          valid = false;
          break;
        }
      }

      if (!valid || inferredTypes.Any(t => t == null))
        continue;

      try
      {
        var constructed = candidate.MakeGenericMethod(inferredTypes);

        // Verify the resulting parameter types.
        var constructedParams = constructed.GetParameters();

        for (var i = 0; i < constructedParams.Length; i++)
        {
          if (constructedParams[i].ParameterType != parameterTypes[i])
          {
            valid = false;
            break;
          }
        }

        if (valid)
          return constructed;
      }
      catch (ArgumentException)
      {
        // Generic constraints weren't satisfied.
      }
    }

    return null;
  }
}
