using System.Text;
using System.Text.Json;
using Holodeck.Web.Management;
using Xunit;

namespace Holodeck.Tests;

public sealed class ManagementPayloadTests
{
  [DatabaseFact]
  public async Task PayloadLimitAcceptsExactBytesAndRejectsLargerOrMultibyteInput()
  {
    await using TestDatabase database = await TestDatabase.CreateAsync();
    await using ManagementFixture fixture = await ManagementFixture.StartAsync(database);
    File.WriteAllText(Path.Combine(fixture.Directory, "input.schema.json"), """{"type":"object","properties":{"text":{"type":"string"}},"required":["text"],"additionalProperties":false}""");
    const int EnvelopeBytes = 11;
    JsonElement boundary = ManagementFixture.Value(new string('x', ManagementLimits.MaxPayloadBytes - EnvelopeBytes));
    Assert.Equal(ManagementLimits.MaxPayloadBytes, Encoding.UTF8.GetByteCount(boundary.GetRawText()));
    ManagementEnqueue input = (await fixture.InputAsync()) with { Input = boundary };
    Assert.False((await fixture.Store.EnqueueAsync(fixture.Setup.B, input)).IsReplay);
    JsonElement oversized = ManagementFixture.Value(new string('x', ManagementLimits.MaxPayloadBytes - EnvelopeBytes + 1));
    await Assert.ThrowsAsync<ArgumentException>(() => fixture.Store.EnqueueAsync(fixture.Setup.B, input with { RequestId = "too-large", Input = oversized }));
    using JsonDocument unicode = JsonDocument.Parse("{\"text\":\"" + new string('é', ManagementLimits.MaxPayloadBytes / 2) + "\"}");
    Assert.True(unicode.RootElement.GetRawText().Length < ManagementLimits.MaxPayloadBytes);
    await Assert.ThrowsAsync<ArgumentException>(() => fixture.Store.EnqueueAsync(fixture.Setup.B, input with { RequestId = "multibyte", Input = unicode.RootElement }));
    Assert.Equal(1, await database.CountAsync("SELECT count(*) FROM management_jobs"));
  }
}
