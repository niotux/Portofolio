namespace Holodeck.Web.Computers;

public sealed record LibraryEnvironmentHostConfig(List<string> Binds, long Memory, long NanoCpus, long PidsLimit, List<string> CapDrop, List<string> CapAdd, List<string> SecurityOpt, bool Init, string NetworkMode, Dictionary<string, List<ContainerPortBinding>> PortBindings, Dictionary<string, string> Tmpfs);
