namespace Humans;

public sealed record SavedSession(string Summary, int Dropped, List<Turn> Turns);
