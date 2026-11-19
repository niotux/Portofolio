using System.Text.Json;

namespace Humans;

// After a thought, the hands act on what the person means to do with the controls the body actually has. This is the one
// pass that emits an operation; the call is not kept, only its experienced result becomes history.
public sealed class Hands(ChatModel model, Page page, RuntimeOptions options)
{
  public async Task<HandsChoice> ChooseAsync(string recent, Surroundings world, Room? focus, CancellationToken token)
  {
    List<Tool> tools = Controls(world.Rooms, focus);
    string situation = recent + "\n" + Situation(focus);
    List<Message> messages = [new Message("system", page.Instruction("hands.txt")), new Message("user", situation)];
    ToolCall? call = await model.ChooseAsync(messages, tools, options.HandsTemperature, token);
    if (call is null)
    {
      // No control chosen is no action: the person goes on thinking. It is never read as a decision to rest.
      return new HandsChoice(HandsActions.Think, "");
    }
    string name = call.Function.Name;
    if (name == HandsActions.Open)
    {
      return new HandsChoice(name, Argument(call.Function.Arguments, "conversation"));
    }
    if (name == HandsActions.Work)
    {
      return new HandsChoice(name, Argument(call.Function.Arguments, "question"));
    }
    if (name == HandsActions.Name)
    {
      return new HandsChoice(name, Argument(call.Function.Arguments, "name"));
    }
    bool known = name == HandsActions.Write || name == HandsActions.Check || name == HandsActions.Close || name == HandsActions.Think || name == HandsActions.Wait || name == HandsActions.Mute || name == HandsActions.Unmute;
    return new HandsChoice(known ? name : HandsActions.Think, "");
  }

  private string Situation(Room? focus)
  {
    string line = focus is null ? page.Controls.Idle : page.Controls.Conversation.Replace("{focus}", focus.Label);
    return page.Fill(line);
  }

  private List<Tool> Controls(Dictionary<string, Room> rooms, Room? focus)
  {
    ControlText text = page.Controls;
    List<Tool> tools = new();
    // The conversation already open is not offered again: reading on happens by itself while it is open.
    List<string> labels = new();
    foreach (Room room in rooms.Values)
    {
      if (focus is null || room.ChannelId != focus.ChannelId)
      {
        labels.Add(room.Label);
      }
    }
    if (labels.Count > 0)
    {
      string schema = JsonSerializer.Serialize(new OpenParameters("object", new OpenProperties(new ConversationProperty("string", labels)), ["conversation"]));
      tools.Add(Control(HandsActions.Open, page.Fill(text.Open), schema));
    }
    if (focus is not null)
    {
      tools.Add(Control(HandsActions.Write, page.Fill(text.Write).Replace("{focus}", focus.Label), Empty));
      tools.Add(Control(HandsActions.Close, page.Fill(text.Close).Replace("{focus}", focus.Label), Empty));
      string mute = focus.Muted ? text.Unmute : text.Mute;
      tools.Add(Control(focus.Muted ? HandsActions.Unmute : HandsActions.Mute, page.Fill(mute).Replace("{focus}", focus.Label), Empty));
      if (focus.Unnamed.Length > 0)
      {
        tools.Add(Control(HandsActions.Name, page.Fill(text.Name).Replace("{focus}", focus.Label).Replace("{unknown}", page.Fill(page.Text.Human)), NameSchema));
      }
    }
    tools.Add(Control(HandsActions.Check, page.Fill(text.Check), Empty));
    tools.Add(Control(HandsActions.Work, page.Fill(text.Work), QuestionSchema));
    tools.Add(Control(HandsActions.Think, page.Fill(text.Think), Empty));
    tools.Add(Control(HandsActions.Wait, page.Fill(text.Wait), Empty));
    return tools;
  }

  private const string Empty = """{"type":"object","properties":{}}""";
  private const string QuestionSchema = """{"type":"object","properties":{"question":{"type":"string"}},"required":["question"]}""";
  private const string NameSchema = """{"type":"object","properties":{"name":{"type":"string"}},"required":["name"]}""";

  private static Tool Control(string name, string description, string parameters)
  {
    using JsonDocument schema = JsonDocument.Parse(parameters);
    return new Tool("function", new ToolFunction(name, description, schema.RootElement.Clone()));
  }

  private static string Argument(string arguments, string property)
  {
    try
    {
      using JsonDocument parsed = JsonDocument.Parse(arguments);
      if (parsed.RootElement.ValueKind == JsonValueKind.Object && parsed.RootElement.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.String)
      {
        return value.GetString()!;
      }
    }
    catch (JsonException)
    {
    }
    return "";
  }
}
