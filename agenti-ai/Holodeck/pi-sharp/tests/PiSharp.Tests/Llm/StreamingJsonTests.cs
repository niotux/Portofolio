using System.Text.Json.Nodes;
using PiSharp.Llm.Json;

namespace PiSharp.Tests.Llm;

public sealed class StreamingJsonTests
{
    [Theory]
    [InlineData("", "{}")]
    [InlineData("{", "{}")]
    [InlineData("""{"ci""", "{}")]
    [InlineData("""{"city""", "{}")]
    [InlineData("""{"city":""", "{}")]
    [InlineData("""{"city":"Par""", """{"city":"Par"}""")]
    [InlineData("{\"city\":\"Paris\"", """{"city":"Paris"}""")]
    [InlineData("""{"city":"Paris",""", """{"city":"Paris"}""")]
    [InlineData("""{"city":"Paris","days":""", """{"city":"Paris"}""")]
    [InlineData("""{"city":"Paris","days":3""", """{"city":"Paris"}""")]
    [InlineData("""{"city":"Paris","days":3,""", """{"city":"Paris","days":3}""")]
    [InlineData("""{"city":"Paris","days":3}""", """{"city":"Paris","days":3}""")]
    [InlineData("""{"ok":tr""", "{}")]
    [InlineData("""{"ok":true,"n":nul""", """{"ok":true}""")]
    [InlineData("""{"list":[1,2,""", """{"list":[1,2]}""")]
    [InlineData("""{"list":["a","b""", """{"list":["a","b"]}""")]
    [InlineData("""{"nested":{"a":{"b":"c""", """{"nested":{"a":{"b":"c"}}}""")]
    [InlineData("""{"text":"line\n""", """{"text":"line\n"}""")]
    [InlineData("{\"text\":\"esc\\\"", """{"text":"esc\""}""")]
    public void ParseObject_HandlesTruncatedInput(string input, string expected)
    {
        var actual = StreamingJson.ParseObject(input);
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(expected), actual), actual.ToJsonString());
    }

    [Fact]
    public void ParseObject_ReturnsEmptyForNonObjects()
    {
        Assert.Empty(StreamingJson.ParseObject("[1,2]"));
        Assert.Empty(StreamingJson.ParseObject("\"str\""));
        Assert.Empty(StreamingJson.ParseObject("garbage"));
    }

    [Fact]
    public void Repair_FixesModelEscapeMistakes()
    {
        Assert.Equal("""{"a":"x\\d"}""", StreamingJson.Repair("""{"a":"x\d"}"""));           // invalid escape
        Assert.Equal("""{"a":"tab\there"}""", StreamingJson.Repair("{\"a\":\"tab\there\"}"));  // raw control char
        Assert.Equal("""{"a":"ok\n"}""", StreamingJson.Repair("""{"a":"ok\n"}"""));            // valid escape untouched
        Assert.Equal("""{"a":"\u00e9"}""", StreamingJson.Repair("""{"a":"\u00e9"}"""));        // unicode escape untouched
        Assert.Equal("{\"a\":\"end\\\\", StreamingJson.Repair("{\"a\":\"end\\"));              // trailing backslash

        var clean = """{"a":"b"}""";
        Assert.Same(clean, StreamingJson.Repair(clean));
    }

    [Fact]
    public void Parse_UsesRepairBeforePartial()
    {
        var node = StreamingJson.Parse("""{"path":"C:\Users\x","n":1}""");
        Assert.Equal(@"C:\Users\x", node!["path"]!.GetValue<string>());
        Assert.Equal(1, node["n"]!.GetValue<int>());
    }

    [Theory]
    [InlineData("""{"room":"ops","body":"hi","room":"board"}""", """{"room":"board","body":"hi"}""")]
    [InlineData("""{"about":["a"],"about":["b"],"thread":1}""", """{"about":["b"],"thread":1}""")]
    [InlineData("""{"outer":{"k":1,"k":2}}""", """{"outer":{"k":2}}""")]
    public void ParseObject_DuplicateKeysKeepTheLastValueAndCanBeRead(string input, string expected)
    {
        var actual = StreamingJson.ParseObject(input);

        // Count is the first read that threw on the seat (ToolCatalog.Canonical).
        Assert.Equal(JsonNode.Parse(expected)!.AsObject().Count, actual.Count);
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(expected), actual), actual.ToJsonString());
    }

    [Fact]
    public void ParseWithRepair_ThrowsOnHopelessInput()
    {
        Assert.ThrowsAny<Exception>(() => StreamingJson.ParseWithRepair("{\"a\":"));
    }
}
