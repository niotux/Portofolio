namespace PiSharp.Tests.Harness;

/// <summary>Serializes tests that mutate the process-wide PI_FACTORY_TOKEN environment variable.</summary>
[CollectionDefinition("cubicle-env", DisableParallelization = true)]
public sealed class CubicleEnvironmentCollection;
