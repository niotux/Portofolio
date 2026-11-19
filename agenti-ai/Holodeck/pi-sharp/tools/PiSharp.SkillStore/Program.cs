using PiSharp.SkillStore;

if (args.Length < 1)
{
    Console.Error.WriteLine("usage: PiSharp.SkillStore <skills-root> [alias]");
    return 1;
}

var root = args[0];
var alias = args.Length > 1 ? args[1] : "store";
await SkillStoreHost.RunStdioAsync(root, alias);
return 0;
