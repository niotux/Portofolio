namespace OpenUL.Manshell;

public interface IManshellConsole
{
  string ReadLine();
  void Clear();
  ConsoleColor Foreground { get; set; }
  ConsoleColor Background { get; set; }
  void ResetColor();
  void PrintException(Exception ex);
  void PrintResult(object? obj);
  void PrintObject(object? obj);
  TextWriter StdOut { get; }
  TextWriter StdErr { get; }
  TextReader StdIn { get; }
  (int Width, int Height) Size { get; }
}