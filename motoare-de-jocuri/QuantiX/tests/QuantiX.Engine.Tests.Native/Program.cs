using System.Runtime.InteropServices;
using QuantiX.Engine.Native;
using QuantiX.Engine.Native.Memory;
using QuantiX.Engine.Native.Utility;

Ref<Record> owner = default;

{
  using var obj = RefFactory.New<Record, (int, int, int)>((4, 4, 5));
  PrintRecord(obj);
  owner = obj.Pass();
  obj.Value.Value = 66;
  PrintRecord(owner);
  obj.Value.Value++;
  PrintRecord(obj);
}

PrintRecord(owner);
owner.Dispose();

static void PrintRecord(Ref<Record> record)
{
  record.Pass();
  Console.WriteLine(record.Value.ToString());
  record.Dispose();
}

[StructLayout(LayoutKind.Sequential)]
public record struct Record : IObjectCtor<(int Value, int Id, int Age)>
{
  public int Value { get; set; }
  public int Id { get; private set; }
  public int Age { get; private set; }

  public void New((int Value, int Id, int Age) p)
  {
    (Value, Id, Age) = p;
  }
}