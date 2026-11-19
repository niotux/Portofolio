using System.ComponentModel;
using Holodeck.Web.Rooms;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Holodeck.Mcp;

[McpServerToolType]
public sealed class ChannelTools(HolodeckClient holodeck)
{
  public const int MaxWaitSeconds = 120;
  private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);

  [McpServerTool(Name = "create_channel")]
  [Description("Create a channel with the given members. Include You's member id to speak in it.")]
  public Task<string> CreateChannelAsync(string projectId, string name, [Description("Member ids from room_view.")] List<string> memberIds, CancellationToken token)
  {
    return holodeck.SendJsonAsync(HttpMethod.Post, $"{ApiRoutes.Room(projectId)}/channels", new CreateChannelInput(name, memberIds), token);
  }

  [McpServerTool(Name = "update_channel", Idempotent = true)]
  [Description("Rename a channel or pause it. A paused channel holds agents' posts; You can still post.")]
  public Task<string> UpdateChannelAsync(string projectId, string channelId, [Description("New name, or omit.")] string? name = null, [Description("Pause or resume, or omit.")] bool? isPaused = null, CancellationToken token = default)
  {
    return holodeck.SendJsonAsync(HttpMethod.Put, ApiRoutes.Channel(projectId, channelId), new ChannelInput(name, isPaused), token);
  }

  [McpServerTool(Name = "delete_channel", Destructive = true)]
  [Description("Delete a channel and its history.")]
  public Task<string> DeleteChannelAsync(string projectId, string channelId, CancellationToken token)
  {
    return holodeck.SendAsync(HttpMethod.Delete, ApiRoutes.Channel(projectId, channelId), token);
  }

  [McpServerTool(Name = "add_channel_member", Idempotent = true)]
  [Description("Add a member (an agent, or You) to a channel.")]
  public Task<string> AddChannelMemberAsync(string projectId, string channelId, string memberId, CancellationToken token)
  {
    return holodeck.SendJsonAsync(HttpMethod.Post, $"{ApiRoutes.Channel(projectId, channelId)}/members", new ChannelMemberInput(memberId), token);
  }

  [McpServerTool(Name = "remove_channel_member", Destructive = true)]
  [Description("Remove a member from a channel. They stop receiving its messages.")]
  public Task<string> RemoveChannelMemberAsync(string projectId, string channelId, string memberId, CancellationToken token)
  {
    return holodeck.SendAsync(HttpMethod.Delete, $"{ApiRoutes.Channel(projectId, channelId)}/members/{HolodeckClient.Segment(memberId)}", token);
  }

  [McpServerTool(Name = "read_channel", ReadOnly = true)]
  [Description("Read a channel's messages in order, one page at a time. Pass the last id you have as after to read on.")]
  public Task<string> ReadChannelAsync(string projectId, string channelId, [Description("Return messages with ids above this; omit for the start.")] long? after = null, CancellationToken token = default)
  {
    return holodeck.GetAsync(PacketsPath(projectId, channelId, after ?? 0), token);
  }

  [McpServerTool(Name = "send_message")]
  [Description("Post a message as You, optionally with local files attached. You must be a member of the channel.")]
  public Task<string> SendMessageAsync(string projectId, string channelId, string note, [Description("Absolute paths of local files to attach.")] List<string>? files = null, CancellationToken token = default)
  {
    return holodeck.SendFormAsync($"{ApiRoutes.Channel(projectId, channelId)}/packets", note, files ?? new(), token);
  }

  [McpServerTool(Name = "wait_for_messages", ReadOnly = true)]
  [Description("Wait until someone other than You posts in a channel after a given message id, then return every message after it. Returns arrived=false when the time runs out.")]
  public async Task<WaitResult> WaitForMessagesAsync(string projectId, string channelId, [Description("Seconds to wait, 1 to 120.")] int timeoutSeconds, [Description("Usually the id send_message returned; omit to wait for anything newer than the latest message.")] long? after = null, CancellationToken token = default)
  {
    if (timeoutSeconds < 1 || timeoutSeconds > MaxWaitSeconds)
    {
      throw new McpException($"Wait between 1 and {MaxWaitSeconds} seconds.");
    }

    string youId = await YouIdAsync(projectId, token);
    DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(timeoutSeconds);
    long cursor = after ?? await LatestIdAsync(projectId, channelId, token);
    List<Packet> received = new();
    while (true)
    {
      List<Packet> page = await holodeck.GetJsonAsync<List<Packet>>(PacketsPath(projectId, channelId, cursor), token);
      bool fromOthers = false;
      foreach (Packet packet in page)
      {
        received.Add(packet);
        cursor = packet.Id;
        if (packet.SenderId != youId)
        {
          fromOthers = true;
        }
      }

      if (fromOthers)
      {
        return new WaitResult(true, cursor, received);
      }

      bool hasMore = page.Count == RoomLimits.HistoryPageSize;
      if (hasMore)
      {
        continue;
      }

      if (DateTimeOffset.UtcNow >= deadline)
      {
        return new WaitResult(false, cursor, received);
      }

      await Task.Delay(PollInterval, token);
    }
  }

  [McpServerTool(Name = "read_message_file", ReadOnly = true)]
  [Description("Read a file attached to a message. Small UTF-8 text comes back inline; other files are saved locally and the answer gives the path.")]
  public Task<string> ReadMessageFileAsync(string projectId, [Description("The message id.")] long packetId, [Description("The file name as the message lists it.")] string name, CancellationToken token)
  {
    return holodeck.DownloadAsync($"{ApiRoutes.Room(projectId)}/packets/{packetId}/files/{HolodeckClient.Segment(name)}", name, token);
  }

  private async Task<string> YouIdAsync(string projectId, CancellationToken token)
  {
    RoomView room = await holodeck.GetJsonAsync<RoomView>(ApiRoutes.Room(projectId), token);
    foreach (RoomMemberView member in room.Members)
    {
      if (member.IsYou)
      {
        return member.Id;
      }
    }

    throw new McpException("This room has no You member.");
  }

  private async Task<long> LatestIdAsync(string projectId, string channelId, CancellationToken token)
  {
    long cursor = 0;
    while (true)
    {
      List<Packet> page = await holodeck.GetJsonAsync<List<Packet>>(PacketsPath(projectId, channelId, cursor), token);
      if (page.Count > 0)
      {
        cursor = page[page.Count - 1].Id;
      }

      if (page.Count < RoomLimits.HistoryPageSize)
      {
        return cursor;
      }
    }
  }

  private static string PacketsPath(string projectId, string channelId, long after)
  {
    return $"{ApiRoutes.Channel(projectId, channelId)}/packets?after={after}";
  }
}
