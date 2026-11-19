using System.Net.Http.Headers;
using System.Text;
using System.Net.Http.Json;
using System.Text.Json;

namespace Humans;

// JSON here is only HTTP transport. Completion text is returned untouched: no output schema, parser, tools or fields.
// Both instructed passes and page continuation use the OpenAI Chat Completions protocol.
public sealed class ChatModel(HttpClient http, ModelOptions options)
{
  private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

  public Task<Completion> SayAsync(List<Message> messages, CancellationToken token, Action<string>? onText = null, double temperature = -1)
  {
    return CompleteAsync(messages, null, options.MaxOutputTokens, onText is not null, token, onText, temperature);
  }

  // The page remains prose. A library instruction asks a chat model to write only its continuation.
  public Task<Completion> ContinueAsync(string page, string instruction, List<string> stop, int maxTokens, CancellationToken token, Action<string>? onText = null, double temperature = -1)
  {
    List<Message> messages = [new Message("system", instruction), new Message("user", page)];
    return CompleteAsync(messages, stop, maxTokens > 0 ? maxTokens : options.MaxOutputTokens, true, token, onText, temperature);
  }

  private async Task<Completion> CompleteAsync(List<Message> messages, List<string>? stop, int maxTokens, bool stream, CancellationToken token, Action<string>? onText, double temperature)
  {
    using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
    timeout.CancelAfter(TimeSpan.FromSeconds(options.TimeoutSeconds));
    // Native thinking is disabled even for older saved settings; it would answer as an assistant about the person.
    ChatBody body = new(options.Name, messages, temperature >= 0 ? temperature : options.Temperature, maxTokens, stream, "none", stream ? new StreamOptions(true) : null, stop);
    using HttpResponseMessage response = await PostAsync(Address("chat/completions"), JsonContent.Create(body, options: Json), timeout.Token);
    if (response.Content.Headers.ContentType?.MediaType == "text/event-stream")
    {
      return await ReadStreamAsync(response, onText, timeout.Token);
    }

    ChatResponse result = await response.Content.ReadFromJsonAsync<ChatResponse>(Json, timeout.Token) ?? throw new IOException("The chat endpoint returned no completion.");
    if (result.Choices is null)
    {
      throw new IOException("The chat endpoint returned no choices.");
    }
    if (result.Choices.Count == 0 || result.Choices[0].Message is null || result.Choices[0].Message.Content is null)
    {
      throw new IOException("The chat endpoint returned no text.");
    }

    Usage usage = new(result.Usage?.PromptTokens ?? 0, result.Usage?.CompletionTokens ?? 0);
    string text = result.Choices[0].Message.Content;
    onText?.Invoke(text);
    return new Completion(text, usage);
  }

  private async Task<HttpResponseMessage> PostAsync(Uri address, HttpContent content, CancellationToken token)
  {
    using HttpRequestMessage request = new(HttpMethod.Post, address);
    request.Content = content;
    if (!string.IsNullOrEmpty(options.ApiKey))
    {
      request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);
    }
    HttpResponseMessage response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
    if (!response.IsSuccessStatusCode)
    {
      response.Dispose();
      throw new HttpRequestException($"Model returned HTTP {(int)response.StatusCode}; check the Model settings.");
    }
    return response;
  }

  private static async Task<Completion> ReadStreamAsync(HttpResponseMessage response, Action<string>? onText, CancellationToken token)
  {
    using StreamReader reader = new(await response.Content.ReadAsStreamAsync(token));
    StringBuilder text = new();
    Usage usage = new(0, 0);
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
      StreamChunk chunk = JsonSerializer.Deserialize<StreamChunk>(data, Json) ?? throw new IOException("The model stream returned an empty event.");
      if (chunk.Usage is not null)
      {
        usage = new Usage(chunk.Usage.PromptTokens, chunk.Usage.CompletionTokens);
      }
      if (chunk.Choices is null)
      {
        continue;
      }
      foreach (StreamChoice choice in chunk.Choices)
      {
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
      throw new IOException("The model stream ended before completion; partial text was not sent as a reply.");
    }
    return new Completion(text.ToString(), usage);
  }

  private Uri Address(string route)
  {
    return new Uri(new Uri(options.BaseUrl.TrimEnd('/') + "/"), route);
  }
}
