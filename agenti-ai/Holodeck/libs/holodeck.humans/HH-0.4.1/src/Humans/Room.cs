namespace Humans;

// A conversation as the body knows it: Label is how the person sees it, Others who else is in it, Unnamed the member the
// person has no name for yet ("" when everyone has one), Muted whether only calls to the person buzz.
public sealed record Room(string ChannelId, string Label, bool Direct, List<string> Others, string Unnamed, bool Muted);
