using PiSharp.Cli;

try
{
    Console.InputEncoding = System.Text.Encoding.UTF8;
    Console.OutputEncoding = System.Text.Encoding.UTF8;
}
catch (IOException)
{
    // redirected or detached console
}

// A seat's stderr is its log file: each line begins with its time (StampedWriter, 2026-10-30).
if (Console.IsErrorRedirected && !args.Contains("--version"))
    Console.SetError(TextWriter.Synchronized(new StampedWriter(Console.Error)));

// A newer build this seat staged itself runs in this one's place (pi-sharp 0.5.99 self-update hand-over).
if (!args.Contains("--version") && SelfUpdater.HandOverTarget(SelfUpdater.DefaultRoot(), CliHost.Version, line => Console.Error.WriteLine(line)) is { } staged)
{
    Console.Error.WriteLine($"self-update: handing over to {staged}");
    return await SelfUpdater.HandOverAsync(staged, args);
}
if (NotebookCommand.Handles(args)) return NotebookCommand.Run(args, Console.Out, Console.Error);
if (GuardCommand.Handles(args)) return GuardCommand.Run(args, Console.Out, Console.Error);
return await CliHost.RunAsync(args);
