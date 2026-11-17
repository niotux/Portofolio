using System;
using System.Diagnostics;
using System.IO;

namespace Texp.Fs;

/// <summary>Starts other programs: short helpers we wait for, and apps that live on after texp exits.</summary>
public static class Launcher
{
  /// <summary>Tried in order when $TERMINAL isn't set.</summary>
  static readonly string[] Terminals = ["x-terminal-emulator", "gnome-terminal", "kgx", "ptyxis", "konsole", "kitty", "alacritty", "ghostty", "wezterm", "foot", "xterm"];

  public readonly record struct Result
  (
    int Exit,
    string Stdout,
    string Stderr
  )
  {
    public bool Ok
    {
      get { return Exit == 0; }
    }

    /// <summary>The first non-empty stderr line, for a one-line error message.</summary>
    public string ErrorLine()
    {
      ReadOnlySpan<char> s = Stderr.AsSpan().Trim();
      int nl = s.IndexOf('\n');
      if (nl >= 0)
      {
        s = s.Slice(0, nl);
      }
      return s.IsEmpty ? "exit code " + Exit : s.ToString();
    }
  }

  /// <summary>Full path of <paramref name="name"/> on $PATH, or null.</summary>
  public static string? FindOnPath(string name)
  {
    if (name.AsSpan().Contains('/'))
    {
      return File.Exists(name) ? name : null;
    }
    ReadOnlySpan<char> path = Environment.GetEnvironmentVariable("PATH");
    while (!path.IsEmpty)
    {
      int colon = path.IndexOf(':');
      ReadOnlySpan<char> dir = colon < 0 ? path : path.Slice(0, colon);
      path = colon < 0 ? ReadOnlySpan<char>.Empty : path.Slice(colon + 1);
      if (dir.IsEmpty)
      {
        continue;
      }
      string candidate = Path.Join(dir, name);
      if (File.Exists(candidate))
      {
        return candidate;
      }
    }
    return null;
  }

  /// <summary>Runs a helper to completion and captures its output.</summary>
  public static Result Run(string file, string[] args)
  {
    ProcessStartInfo psi = new(file);
    for (int i = 0; i < args.Length; i++)
    {
      psi.ArgumentList.Add(args[i]);
    }
    psi.RedirectStandardInput = true;
    psi.RedirectStandardOutput = true;
    psi.RedirectStandardError = true;
    psi.UseShellExecute = false;
    try
    {
      using Process p = Process.Start(psi)!;
      p.StandardInput.Close();
      System.Threading.Tasks.Task<string> err = p.StandardError.ReadToEndAsync();
      string output = p.StandardOutput.ReadToEnd();
      p.WaitForExit();
      return new Result(p.ExitCode, output, err.Result);
    }
    catch (Exception e)
    {
      return new Result(-1, "", e.Message);
    }
  }

  /// <summary>
  /// Starts <paramref name="file"/> in its own session with stdio on /dev/null, so it never draws over
  /// the TUI and keeps running after texp exits. Returns an error, or null.
  /// </summary>
  public static string? Detached(string file, string[] args, string workingDirectory)
  {
    if (FindOnPath(file) == null)
    {
      return file + " not found";
    }
    ProcessStartInfo psi = new("sh");
    psi.ArgumentList.Add("-c");
    psi.ArgumentList.Add("exec setsid -f \"$@\" </dev/null >/dev/null 2>&1");
    psi.ArgumentList.Add("sh");
    psi.ArgumentList.Add(file);
    for (int i = 0; i < args.Length; i++)
    {
      psi.ArgumentList.Add(args[i]);
    }
    psi.WorkingDirectory = workingDirectory;
    psi.UseShellExecute = false;
    psi.RedirectStandardInput = true;
    try
    {
      Process p = Process.Start(psi)!;
      p.StandardInput.Close();
      // setsid -f returns right away; reap it so it doesn't linger as a zombie.
      _ = p.WaitForExitAsync().ContinueWith(_ => p.Dispose());
      return null;
    }
    catch (Exception e)
    {
      return e.Message;
    }
  }

  /// <summary>Opens a file with its default app, like double-clicking it in GNOME Files.</summary>
  public static string? Open(string path)
  {
    return Detached("xdg-open", [path], Path.GetDirectoryName(path) ?? "/");
  }

  /// <summary>The terminal to use: $TERMINAL, else the first known one installed. Null when there is none.</summary>
  public static string? Terminal()
  {
    string? env = Environment.GetEnvironmentVariable("TERMINAL");
    if (!string.IsNullOrEmpty(env) && FindOnPath(env) != null)
    {
      return env;
    }
    for (int i = 0; i < Terminals.Length; i++)
    {
      if (FindOnPath(Terminals[i]) != null)
      {
        return Terminals[i];
      }
    }
    return null;
  }

  public static string? OpenTerminal(string folder)
  {
    return InTerminal(folder, []);
  }

  /// <summary>
  /// Runs <paramref name="program"/> in a new terminal window. The window stays open when it ends, showing the exit
  /// code, so its output can be read.
  /// </summary>
  public static string? RunInTerminal(string program, string folder)
  {
    const string Script = "\"$1\"; s=$?; printf '\\n[%s exited with code %d. Press Enter to close.]' \"${1##*/}\" \"$s\"; read _";
    return InTerminal(folder, ["sh", "-c", Script, "sh", program]);
  }

  /// <summary>Opens a terminal in <paramref name="folder"/>, running <paramref name="command"/> if there is one.</summary>
  static string? InTerminal(string folder, string[] command)
  {
    string? terminal = Terminal();
    if (terminal == null)
    {
      return "no terminal found; set $TERMINAL";
    }
    ReadOnlySpan<char> name = Path.GetFileName(terminal.AsSpan());
    string[] prefix = [];
    if (command.Length > 0)
    {
      prefix = ExecPrefix(name);
    }
    // gnome-terminal talks to a running server, which ignores our cwd unless told.
    bool gnome = name.SequenceEqual("gnome-terminal") || name.SequenceEqual("x-terminal-emulator");
    string[] args = new string[(gnome ? 1 : 0) + prefix.Length + command.Length];
    int n = 0;
    if (gnome)
    {
      args[n++] = "--working-directory=" + folder;
    }
    prefix.CopyTo(args, n);
    n += prefix.Length;
    command.CopyTo(args, n);
    return Detached(terminal, args, folder);
  }

  /// <summary>What goes between a terminal's name and the program it should run; terminals disagree.</summary>
  static string[] ExecPrefix(ReadOnlySpan<char> terminal)
  {
    if (terminal.SequenceEqual("gnome-terminal") || terminal.SequenceEqual("kgx") || terminal.SequenceEqual("ptyxis"))
    {
      return ["--"];
    }
    if (terminal.SequenceEqual("kitty") || terminal.SequenceEqual("foot"))
    {
      return [];
    }
    if (terminal.SequenceEqual("wezterm"))
    {
      return ["start", "--"];
    }
    // x-terminal-emulator, konsole, alacritty, ghostty, xterm and most others.
    return ["-e"];
  }
}
