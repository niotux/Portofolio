namespace Holodeck.Mcp;

// Routes under a project's room, with every id escaped.
public static class ApiRoutes
{
  public static string Room(string projectId)
  {
    return $"/api/projects/{HolodeckClient.Segment(projectId)}/room";
  }

  public static string Agent(string projectId, string agentId)
  {
    return $"{Room(projectId)}/agents/{HolodeckClient.Segment(agentId)}";
  }

  public static string Channel(string projectId, string channelId)
  {
    return $"{Room(projectId)}/channels/{HolodeckClient.Segment(channelId)}";
  }
}
