namespace Humans;

// Library prose describing each body control to the hands, from controls.json. {focus} names the open conversation; Idle says none is open.
public sealed record ControlText(string Open, string Conversation, string Idle, string Write, string Check, string Close, string Think, string Wait, string Name, string Mute, string Unmute);
