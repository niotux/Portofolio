namespace PrimordialPower.Tools;

public interface IInspectorServices
{
  (Type? Type, string[]? Usings) Context { set; }
}