using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace CreativeStudio;

// Ordinary speech stays prose. Only a focused hands pass receives tool controls.
public sealed class CreativeModel(HttpClient http, ModelOptions options)
{
  private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

  public async Task<ModelCompletion> SayAsync(List<ChatMessage> messages, CancellationToken token, Action<string>? onText = null)
  {
    using CancellationTokenSource timeout = RequestTimeout(token);
    bool stream = onText is not null;
    ChatRequest body = Request(messages, stream, null);
    using HttpResponseMessage response = await PostAsync(body, timeout.Token);
    if (response.Content.Headers.ContentType?.MediaType == "text/event-stream")
    {
      return await ReadStreamAsync(response, onText, timeout.Token);
    }

    ChatResponse result = await ReadResponseAsync(response, timeout.Token);
    ChatChoice choice = FirstChoice(result);
    RequireComplete(choice.FinishReason);
    ChatResponseMessage message = choice.Message!;
    if (message.ToolCalls is not null)
    {
      if (message.ToolCalls.Count > 0)
      {
        throw new IOException("The conversation pass returned an operation without tool controls.");
      }
    }

    string text = message.Content ?? "";
    onText?.Invoke(text);
    return new ModelCompletion(text, Usage(result.Usage));
  }

  public async Task<ModelAction> ActAsync(List<ChatMessage> messages, List<ToolDefinition> tools, CancellationToken token)
  {
    if (tools.Count == 0)
    {
      ModelCompletion speech = await SayAsync(messages, token);
      return new ModelAction(speech.Text, new List<ToolCall>(), speech.Usage);
    }

    using CancellationTokenSource timeout = RequestTimeout(token);
    ChatRequest body = Request(messages, false, tools);
    using HttpResponseMessage response = await PostAsync(body, timeout.Token);
    ChatResponse result = await ReadResponseAsync(response, timeout.Token);
    ChatChoice choice = FirstChoice(result);
    RequireComplete(choice.FinishReason, true);
    ChatResponseMessage message = choice.Message!;
    List<ToolCall> calls = ReadCalls(message.ToolCalls, tools);
    return new ModelAction(message.Content ?? "", calls, Usage(result.Usage));
  }

  private ChatRequest Request(List<ChatMessage> messages, bool stream, List<ToolDefinition>? tools)
  {
    // Always disabled, including stale saved settings that used native reasoning in another agent.
    return new ChatRequest(options.Name, messages, options.Temperature, options.MaxOutputTokens, stream, new ThinkingOptions(false), tools, tools is null ? null : "auto", stream ? new ChatStreamOptions(true) : null);
  }

  private CancellationTokenSource RequestTimeout(CancellationToken token)
  {
    CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
    timeout.CancelAfter(TimeSpan.FromSeconds(options.TimeoutSeconds));
    return timeout;
  }

  private async Task<HttpResponseMessage> PostAsync(ChatRequest body, CancellationToken token)
  {
    Uri address = new(new Uri(options.BaseUrl.TrimEnd('/') + "/"), "chat/completions");
    using HttpRequestMessage request = new(HttpMethod.Post, address);
    request.Content = JsonContent.Create(body, options: Json);
    if (!string.IsNullOrEmpty(options.ApiKey))
    {
      request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);
    }

    HttpResponseMessage response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
    if (!response.IsSuccessStatusCode)
    {
      int status = (int)response.StatusCode;
      response.Dispose();
      throw new HttpRequestException($"The model returned HTTP {status}. Check the model connection.");
    }
    return response;
  }

  private static async Task<ChatResponse> ReadResponseAsync(HttpResponseMessage response, CancellationToken token)
  {
    try
    {
      return await response.Content.ReadFromJsonAsync<ChatResponse>(Json, token) ?? throw new IOException("The model returned an empty response.");
    }
    catch (JsonException exception)
    {
      throw new IOException("The model returned an invalid response.", exception);
    }
  }

  private static ChatChoice FirstChoice(ChatResponse result)
  {
    if (result.Choices is null)
    {
      throw new IOException("The model returned no choices.");
    }
    if (result.Choices.Count == 0)
    {
      throw new IOException("The model returned no choices.");
    }
    ChatChoice choice = result.Choices[0];
    if (choice.Message is null)
    {
      throw new IOException("The model returned no message.");
    }
    return choice;
  }

  private static void RequireComplete(string? reason, bool tools = false)
  {
    if (reason == "length")
    {
      throw new IOException("The model reached its output allowance; the partial response was not used.");
    }
    if (reason != "stop" && !(tools && reason == "tool_calls"))
    {
      throw new IOException("The model did not complete its response.");
    }
  }

  private static List<ToolCall> ReadCalls(List<ToolResponseCall>? returned, List<ToolDefinition> tools)
  {
    List<ToolCall> calls = new();
    if (returned is null)
    {
      return calls;
    }
    foreach (ToolResponseCall call in returned)
    {
      ToolResponseFunction? function = call.Function;
      if (function is null)
      {
        throw new IOException("The model returned an operation without a function.");
      }
      string name = function.Name ?? "";
      if (call.Type != "function" || !KnownTool(name, tools))
      {
        throw new IOException("The model selected an unavailable tool.");
      }
      try
      {
        using JsonDocument arguments = JsonDocument.Parse(function.Arguments ?? "");
        if (arguments.RootElement.ValueKind != JsonValueKind.Object)
        {
          throw new IOException("The tool operation has no argument object.");
        }
        HashSet<string> names = new(StringComparer.Ordinal);
        foreach (JsonProperty argument in arguments.RootElement.EnumerateObject())
        {
          if (!names.Add(argument.Name))
          {
            throw new IOException("The tool operation repeats an argument.");
          }
        }
        calls.Add(new ToolCall(call.Id ?? "", name, arguments.RootElement.Clone()));
      }
      catch (JsonException exception)
      {
        throw new IOException("The model returned incomplete tool arguments.", exception);
      }
    }
    return calls;
  }

  private static bool KnownTool(string name, List<ToolDefinition> tools)
  {
    foreach (ToolDefinition tool in tools)
    {
      if (tool.Function.Name == name)
      {
        return true;
      }
    }
    return false;
  }

  private static ModelUsage Usage(ModelUsage? usage) => usage ?? new ModelUsage(0, 0);

  private static async Task<ModelCompletion> ReadStreamAsync(HttpResponseMessage response, Action<string>? onText, CancellationToken token)
  {
    using StreamReader reader = new(await response.Content.ReadAsStreamAsync(token));
    StringBuilder text = new();
    ModelUsage usage = new(0, 0);
    string? finishReason = null;
    bool finished = false;
    string? line;
    while ((line = await reader.ReadLineAsync(token)) is not null)
    {
      if (!line.StartsWith("data:", StringComparison.Ordinal))
      {
        continue;
      }
      string data = line[5..].Trim();
      if (data == "[DONE]")
      {
        finished = true;
        break;
      }
      ChatStreamChunk chunk;
      try
      {
        chunk = JsonSerializer.Deserialize<ChatStreamChunk>(data, Json) ?? throw new IOException("The model stream returned an empty event.");
      }
      catch (JsonException exception)
      {
        throw new IOException("The model stream returned an invalid event.", exception);
      }
      if (chunk.Usage is not null)
      {
        usage = chunk.Usage;
      }
      if (chunk.Choices is null)
      {
        continue;
      }
      foreach (ChatStreamChoice choice in chunk.Choices)
      {
        if (choice.FinishReason is not null)
        {
          finishReason = choice.FinishReason;
        }
        string? content = choice.Delta?.Content;
        if (content is not null)
        {
          text.Append(content);
          onText?.Invoke(content);
        }
      }
    }
    if (!finished)
    {
      throw new IOException("The model stream disconnected; its partial response was not used.");
    }
    RequireComplete(finishReason);
    return new ModelCompletion(text.ToString(), usage);
  }
}
