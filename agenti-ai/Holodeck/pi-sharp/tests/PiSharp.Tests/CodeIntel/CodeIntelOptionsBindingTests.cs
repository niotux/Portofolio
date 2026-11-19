using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PiSharp.CodeIntel;

namespace PiSharp.Tests.CodeIntel;

public sealed class CodeIntelOptionsBindingTests
{
    [Fact]
    public void AddCodeIntelOptions_BindsTheNestedCodeIntelSection()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["CodeIntel:KbUrl"] = "http://codeintel.test:8081",
                ["CodeIntel:WorkspaceRoot"] = "/tmp/codeintel-test-workspaces"
            })
            .Build();
        var services = new ServiceCollection();
        services.AddCodeIntelOptions(configuration);

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<CodeIntelOptions>>().Value;

        Assert.Equal("http://codeintel.test:8081", options.KbUrl);
        Assert.Equal("/tmp/codeintel-test-workspaces", options.WorkspaceRoot);
    }
}
