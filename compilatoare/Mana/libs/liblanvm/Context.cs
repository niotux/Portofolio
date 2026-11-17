namespace LAN.VirtualMachine;


public class Context
{
  internal Context(uint symbolCount)
  {
    Symbols = new Symbol[symbolCount];
  }

  public Stack Stack { get; } = new();
  public Symbol[] Symbols { get; }
}
