using System.Net;
using System.Text;
using System.Text.Json;
using Xunit;

namespace Humans.Tests;

public sealed class PersonFlowTests
{
  [Fact]
  public async Task ModelContentIsProseAndNeverParsedAsAnApplicationRecord()
  {
    ProseHandler handler = new("I don't know yet. A sentence with {braces} is still prose.");
    ChatModel model = new(new HttpClient(handler), new ModelOptions("http://model.invalid/v1/", "test", "", 0.5, 100, 30));
    Completion completion = await model.SayAsync([new Message("system", "I remember in my own words."), new Message("user", "hello")], CancellationToken.None);
    Assert.Equal("I don't know yet. A sentence with {braces} is still prose.", completion.Text);
    using JsonDocument request = JsonDocument.Parse(handler.Body!);
    Assert.False(request.RootElement.TryGetProperty("response_format", out _));
    Assert.False(request.RootElement.TryGetProperty("tools", out _));
    Assert.Equal("hello", request.RootElement.GetProperty("messages")[1].GetProperty("content").GetString());
  }

  [Fact]
  public async Task CompactionRetainsRecentVerbatimTurnsAndFirstPersonThoughts()
  {
    RuntimeOptions options = new(6,2,75,8192,4,6,8,16000,2,3,new EmbeddingOptions("http://memory.invalid/v1/","encoder",2,30),200,0.5,0.6,0.3);
    Conversation chat = new(options);
    for (int i=0;i<3;i++)
    {
      chat.AddUser("their words " + i);
      chat.AddReply("my words " + i,"I remember why this matters " + i,"");
    }
    ChatModel model = new(new HttpClient(new ProseHandler("I remember their earlier uncertainty and the question we left open.")),new ModelOptions("http://model.invalid/v1/","test","",0.5,100,30));
    Assert.True(await chat.CompactAsync(model,"Keep this conversation in my own words.",0.3,CancellationToken.None));
    Assert.Equal(4,chat.FirstKept);
    Assert.Equal(6,chat.Position);
    Assert.Contains("earlier uncertainty",chat.Summary);
    Assert.Equal("their words 2",chat.Turns[0].Text);
    Assert.Equal("I remember why this matters 2",chat.Turns[1].Thought);
  }
}
