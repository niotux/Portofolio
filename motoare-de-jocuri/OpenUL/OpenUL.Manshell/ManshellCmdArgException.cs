using System.Text;

namespace OpenUL.Manshell;

public class ManshellCmdArgException : Exception
{
  public ManshellCmdArgException(ManshellCmd cmd, int arg, string message)
  : base(GetCmdMessage(cmd, arg, message))
  {
  }

  public ManshellCmdArgException(ManshellCmd cmd, int arg, string message, Exception innerException)
  : base(GetCmdMessage(cmd, arg, message), innerException)
  { }

  protected static string GetCmdMessage(ManshellCmd cmd, int arg, string message)
  {
    StringBuilder sb = new();
    sb.Append($"At [{cmd.Names[0]}] (");
    for (int i = 0; i < cmd.Args.Length; i++)
    {
      string type = ManshellArg.GetArgDisplay(cmd.Args[i]).Text;
      if (i != 0)
      {
        sb.Append(", ");
      }
      if (i == arg)
      {
        sb.Append("**");
        sb.Append(type);
        sb.Append("**");
      }
      else
      {
        sb.Append(type);
      }
    }
    sb.Append("): ");
    sb.Append(message);
    return sb.ToString();
  }
}