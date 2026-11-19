namespace Holodeck.Web.Activity;

public sealed record ActivityStep(int Ordinal, string Phase, string Label, string Text, bool IsComplete);
