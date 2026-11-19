// Startup check that calibrates pi-sharp's compaction trigger against the provider's real
// per-request context limit. Shape follows adr-pi-sharp-endpoint-compat (v7) and the scope of
// adr-pi-sharp-ctx-ceiling-check: the declared models.json value is verified against a second
// source the declaration was not copied from (llama.cpp GET /props per-slot n_ctx, or
// vLLM GET /v1/models max_model_len), and the run fails fast when the compaction trigger would
// sit at or above the provider limit - i.e. where compaction could never fire and unattended
// runs die of overflow. A verification that reads the field the declaration was taken from
// cannot fail (the 200192 aggregate error); this reads the per-request bound instead.
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using PiSharp.Core.Models;

namespace PiSharp.Llm;

/// <summary>The outcome of verifying a model's declared context against its provider.</summary>
public sealed record CeilingVerdict
{
    public bool Ok { get; init; }
    /// <summary>Hard input allowance = contextWindow - reserveTokens. A configured working trigger can be lower.</summary>
    public int TriggerTokens { get; init; }
    /// <summary>The provider's per-request bound (per-slot n_ctx or max_model_len), null if it could not be read.</summary>
    public int? ProviderLimit { get; init; }
    /// <summary>True when the trigger is at/above the provider limit: compaction can never fire.</summary>
    public bool TriggerUnreachable { get; init; }
    public string? Error { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = [];

    /// <summary>Provider limit present and the trigger sits below it.</summary>
    public bool Verified => Ok && ProviderLimit is not null && !TriggerUnreachable;
}

public static class ContextCeilingVerifier
{
    /// <summary>
    /// Pure decision. <paramref name="providerLimit"/> is the per-request bound the provider reports
    /// for one request (llama.cpp: per-slot n_ctx, legacy aggregate divided by slots; vLLM: max_model_len). A null limit means the
    /// provider could not be reached or reports no such field: the ceiling is then unverified and the
    /// seat runs on the declared value with a warning - unreachability is a network blip, not a
    /// contradiction, so it never kills a seat. A contradiction (trigger >= limit) is the fail-fast
    /// case.
    /// </summary>
    public static CeilingVerdict Evaluate(int contextWindow, int reserveTokens, int? providerLimit)
    {
        var trigger = contextWindow - reserveTokens;
        if (providerLimit is null)
            return new CeilingVerdict()
            {
                Ok = true,
                TriggerTokens = trigger,
                Error = null,
                Warnings = ["provider limit unavailable; context ceiling unverified, running on the declared value"],
            };

        if (trigger >= providerLimit)
            return new CeilingVerdict()
            {
                Ok = false,
                TriggerTokens = trigger,
                ProviderLimit = providerLimit,
                TriggerUnreachable = true,
                Error = $"compaction trigger {trigger} is at/above the provider limit {providerLimit}: " +
                        $"compaction can never fire and the run will die of context overflow. " +
                        $"Lower contextWindow or raise the provider's per-request limit.",
            };

        return new CeilingVerdict()
        {
            Ok = true,
            TriggerTokens = trigger,
            ProviderLimit = providerLimit,
        };
    }

    /// <summary>
    /// Fetches the provider's per-request bound for the model and applies <see cref="Evaluate"/>.
    /// llama.cpp: <c>GET {host}/props</c> for <c>n_ctx</c> and <c>total_slots</c> (per-agent = n_ctx / total_slots,
    /// integer division; a missing total_slots is treated as 1 and warned about - never silently).
    /// vLLM: <c>GET {host}/v1/models</c> for the matching id's <c>max_model_len</c>.
    /// </summary>
    public static async Task<CeilingVerdict> VerifyAsync(
        ModelConfig model, int reserveTokens, HttpMessageHandler? handler, CancellationToken ct = default)
    {
        using var http = new HttpClient(handler ?? new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(10) };
        if (model.ResolveApiKey() is { Length: > 0 } key)
            http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", key);
        var host = HostOf(model.BaseUrl);
        var warnings = new List<string>();

        // llama.cpp exposes /props with the slot math; try it first. A 200 with an n_ctx field means
        // this is a llama.cpp server and the per-request bound is n_ctx / total_slots.
        var (perRequest, isLlama) = await TryLlamaCppAsync(http, host, model, warnings, ct).ConfigureAwait(false);

        if (!isLlama)
        {
            // Not llama.cpp (or no /props): use /v1/models. This also enforces the ADR's "model id
            // missing from the list => error" rule, which the llama.cpp path skips because /props is
            // server-wide, not per-model.
            var (limit, found) = await ReadVllmLimitAsync(http, host, model, ct).ConfigureAwait(false);
            if (!found)
                return WithWarnings(new CeilingVerdict()
                {
                    Ok = false,
                    TriggerTokens = model.ContextWindow - reserveTokens,
                    Error = $"model '{model.Id}' not reported by {host}/v1/models; refusing to start on an " +
                            $"undecidable context ceiling",
                }, warnings);
            return WithWarnings(Evaluate(model.ContextWindow, reserveTokens, limit), warnings);
        }

        return WithWarnings(Evaluate(model.ContextWindow, reserveTokens, perRequest), warnings);
    }

    private static async Task<(int? Limit, bool Found)> ReadVllmLimitAsync(HttpClient http, string host, ModelConfig model, CancellationToken ct)
    {
        try
        {
            var res = await http.GetAsync($"{host}/v1/models", ct).ConfigureAwait(false);
            if (!res.IsSuccessStatusCode)
                return (null, false);
            var body = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(body);
            var models = doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("data", out var data)
                ? data : doc.RootElement;
            foreach (var item in models.EnumerateArray())
            {
                if (item.TryGetProperty("id", out var idEl) && idEl.GetString() == model.Id)
                {
                    if (item.TryGetProperty("max_model_len", out var m))
                        return (ToInt(m), true);
                    return (null, true); // id present but no max_model_len -> limit unknown
                }
            }
            return (null, false);
        }
        catch
        {
            // Unreachable: warn via caller; a blip is not a contradiction.
            return (null, true);
        }
    }

    private static async Task<(int? PerRequest, bool IsLlama)> TryLlamaCppAsync(
        HttpClient http, string host, ModelConfig model, List<string> warnings, CancellationToken ct)
    {
        HttpResponseMessage res;
        try
        {
            res = await http.GetAsync($"{host}/props", ct).ConfigureAwait(false);
        }
        catch
        {
            return (null, false); // /props unreachable: fall back to /v1/models
        }

        if (!res.IsSuccessStatusCode)
        {
            res.Dispose();
            return (null, false);
        }

        var body = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        res.Dispose();
        try
        {
            var node = JsonNode.Parse(body);
            if (node is not JsonObject obj)
                return (null, false);
            // Current llama.cpp reports meta.slot_n_ctx here: already PER SLOT, never divide it
            // by total_slots. Source: tools/server/server-context.cpp get_res_props (slot_n_ctx).
            if (obj["default_generation_settings"] is JsonObject generation
                && GetInt(generation, "n_ctx") is > 0 and var perSlot)
                return (perSlot, true);
            var nCtx = GetInt(obj, "n_ctx");
            if (nCtx is null)
                return (null, false); // /props exists but no n_ctx: not the llama.cpp shape we need
            var totalSlots = GetInt(obj, "total_slots") ?? 1;
            if (totalSlots <= 0 || nCtx <= 0) return (null, true);
            if (GetInt(obj, "total_slots") is null)
                warnings.Add($"llama.cpp /props on {host} reports no total_slots; treating it as 1 " +
                             $"(n_ctx {nCtx} may be an aggregate across slots - declare n_ctx/total_slots)");
            return (nCtx / totalSlots, true);
        }
        catch
        {
            return (null, false);
        }
    }

    /// <summary>Reads an int from a JSON node that may be a number or a string (llama.cpp /props
    /// reports n_ctx/total_slots as strings; some builds as numbers).</summary>
    private static int? GetInt(JsonObject obj, string key)
    {
        var node = obj[key];
        if (node is null) return null;
        try
        {
            if (node.GetValueKind() == JsonValueKind.Number) return node.GetValue<int>();
            return int.TryParse(node.GetValue<string>(), out var v) ? v : null;
        }
        catch
        {
            return null;
        }
    }

    private static int? ToPositiveInt(string? raw)
    {
        return int.TryParse(raw, out var v) && v > 0 ? v : null;
    }

    /// <summary>Reads an int from a JSON element that may be a number or a string.</summary>
    private static int? ToInt(JsonElement el)
    {
        try
        {
            if (el.ValueKind == JsonValueKind.Number) return el.GetInt32();
            return int.TryParse(el.GetString(), out var v) ? v : null;
        }
        catch
        {
            return null;
        }
    }

    private static CeilingVerdict WithWarnings(CeilingVerdict v, IReadOnlyList<string> extra)
        => v with { Warnings = v.Warnings.Concat(extra).ToList() };

    /// <summary>Strips a trailing <c>/v1</c> so the same entry works for both <c>/v1/models</c> and llama.cpp <c>/props</c>.</summary>
    public static string HostOf(string baseUrl)
    {
        var host = baseUrl.TrimEnd('/');
        if (host.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
            host = host[..^3];
        return host;
    }
}
