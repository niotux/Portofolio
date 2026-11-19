namespace Humans;

// What the body currently knows of the world: its conversations by channel, and how it calls each member it has met.
public sealed record Surroundings(Dictionary<string, Room> Rooms, Dictionary<string, string> Names);
