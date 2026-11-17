using LAN.Assembler.Pipeline2;

string source = File.ReadAllText(args.Length >= 1 ? args[0] : "file.lan");

Console.ForegroundColor = ConsoleColor.Blue;
Console.Write("--- Source --------------------");
Console.ResetColor();
Console.WriteLine("\n");
Console.WriteLine(source);
Console.WriteLine();
Console.ForegroundColor = ConsoleColor.Red;
Console.Write("--- Result --------------------");
Console.ResetColor();
Console.WriteLine("\n");

var result = source
  | Tokenizer.Run
  | Parser.Run;
var instructions = result.Instrictions;

foreach (var instr in instructions)
{
  Console.WriteLine(instr);
}

result.Logger.WriteMessages(Console.Out.WriteLine);