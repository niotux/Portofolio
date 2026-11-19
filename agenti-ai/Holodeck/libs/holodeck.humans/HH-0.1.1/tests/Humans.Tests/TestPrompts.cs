using System.Text.Json;

namespace Humans.Tests;

public static class TestPrompts
{
  public static PromptBundle Bundle(string instruction)
  {
    PageText page = new("A page", "{self}", "{personality}", "someone", "[Earlier:", "[I recognise:", "[I remember:", "[{name} thinks:", "[{name} considers:", "]");
    Dictionary<string, string> files = new()
    {
      ["page.json"] = JsonSerializer.Serialize(page),
      ["continue.txt"] = instruction,
      ["recognise.txt"] = "recognise instruction",
      ["look.txt"] = "look instruction",
      ["search.txt"] = "search instruction",
      ["remember.txt"] = "remember instruction",
      ["form.txt"] = "form instruction",
      ["compact.txt"] = "compact instruction"
    };
    return new PromptBundle(files);
  }
}
