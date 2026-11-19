using System.Text.Json.Nodes;
using PiSharp.Core.Agent;
using PiSharp.Core.Messages;
using PiSharp.Core.Models;
using PiSharp.Llm.OpenAi;
using PiSharp.Tests.Agent;

namespace PiSharp.Tests.Llm;

public sealed class ConstrainedSamplingTests
{
    [Fact]
    public void MakeStrictJsonSchema_RequiresEveryPropertyAndForbidsAdditional()
    {
        var schema = Fakes.Json("""
        {
          "type": "object",
          "properties": {
            "path": { "type": "string" },
            "offset": { "type": "number" }
          },
          "required": ["path"]
        }
        """);

        var strict = ConstrainedSampling.MakeStrictJsonSchema(schema);

        Assert.False(strict["additionalProperties"]!.GetValue<bool>());
        Assert.Equal(["path", "offset"], strict["required"]!.AsArray().Select(n => n!.GetValue<string>()));
        Assert.Equal("string", strict["properties"]!["path"]!["type"]!.GetValue<string>());
        var offset = strict["properties"]!["offset"]!.AsObject();
        Assert.True(offset.ContainsKey("anyOf"));
        Assert.Equal("number", offset["anyOf"]![0]!["type"]!.GetValue<string>());
        Assert.Equal("null", offset["anyOf"]![1]!["type"]!.GetValue<string>());
    }

    [Fact]
    public void MakeStrictJsonSchema_RejectsRefsAndOpenAdditionalProperties()
    {
        Assert.Throws<UnsupportedStrictJsonSchemaException>(() =>
            ConstrainedSampling.MakeStrictJsonSchema(Fakes.Json("""{"type":"object","properties":{"a":{"$ref":"#/defs/a"}}}""")));
        Assert.Throws<UnsupportedStrictJsonSchemaException>(() =>
            ConstrainedSampling.MakeStrictJsonSchema(Fakes.Json("""{"type":"object","properties":{},"additionalProperties":true}""")));
        Assert.Null(ConstrainedSampling.TryMakeStrictJsonSchema(Fakes.Json("""{"type":"object","properties":{"a":{"oneOf":[{}]}}}""")));
    }

    [Fact]
    public void ConvertTools_EmitsStrictTrueWhenCompatAsksAndSchemaAllows()
    {
        var tool = new DelegateTool
        {
            Name = "read",
            Description = "Read a file",
            Parameters = Fakes.Json("""{"type":"object","properties":{"path":{"type":"string"}},"required":["path"]}"""),
        };
        var model = new ModelConfig
        {
            Id = "m",
            BaseUrl = "http://localhost/v1",
            Compat = new ModelCompat { SupportsStrictMode = true },
        };

        var tools = RequestBuilder.ConvertTools(model, [tool]);
        var fn = tools[0]!["function"]!.AsObject();
        Assert.True(fn["strict"]!.GetValue<bool>());
        Assert.False(fn["parameters"]!["additionalProperties"]!.GetValue<bool>());
    }

    [Fact]
    public void NamedToolChoice_AttachesJsonSchemaResponseFormat()
    {
        var tool = new DelegateTool
        {
            Name = "read",
            Parameters = Fakes.Json("""{"type":"object","properties":{"path":{"type":"string"}},"required":["path"]}"""),
        };
        var model = new ModelConfig { Id = "m", BaseUrl = "http://localhost/v1" };
        var body = RequestBuilder.Build(
            model,
            new LlmContext("", [new UserMessage("q")], [tool]),
            new StreamOptions { ToolChoice = "read" });

        Assert.Equal("read", body["tool_choice"]!["function"]!["name"]!.GetValue<string>());
        Assert.Equal("json_schema", body["response_format"]!["type"]!.GetValue<string>());
        Assert.Equal("read", body["response_format"]!["json_schema"]!["name"]!.GetValue<string>());
        Assert.Equal("read", body["response_format"]!["json_schema"]!["schema"]!["properties"]!["name"]!["enum"]![0]!.GetValue<string>());
    }

    [Fact]
    public void NamedToolChoice_OmitsResponseFormatWhenCompatDisablesIt()
    {
        var tool = new DelegateTool { Name = "read" };
        var model = new ModelConfig
        {
            Id = "m",
            BaseUrl = "http://localhost/v1",
            Compat = new ModelCompat { SupportsJsonSchemaResponseFormat = false },
        };
        var body = RequestBuilder.Build(
            model,
            new LlmContext("", [new UserMessage("q")], [tool]),
            new StreamOptions { ToolChoice = "read" });
        Assert.Null(body["response_format"]);
        Assert.Equal("read", body["tool_choice"]!["function"]!["name"]!.GetValue<string>());
    }
}
