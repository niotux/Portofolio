namespace Holodeck.Web.Computers;

// What an agent asks for in its Agent.json. Requests only: nothing is granted until the user grants it.
public sealed record AgentNeeds(List<int> Ports);
