// Credential lifecycle adapted from pi packages/ai/src/auth/types.ts and
// packages/coding-agent/src/core/auth-storage.ts @ 71dca871b; named connections replace provider keys.
using System.Text;
using System.Text.Json.Nodes;

namespace PiSharp.Llm.Auth;

public sealed record SubscriptionConnectionInfo(string Id, string Name, SubscriptionProvider Provider,
    string? AccountId, DateTimeOffset RefreshAfter);

public interface ISubscriptionCredentialSource
{
    Task<SubscriptionCredential> GetAsync(string connectionId, SubscriptionProvider provider, CancellationToken ct);
}

/// <summary>
/// Host-owned secret directory. Linux mode 0700/0600; Windows inherits the operator profile ACL.
/// Run a shared host under a separate OS identity from agent seats. Same-user shell tools cannot
/// be isolated from this directory by file permissions alone.
/// </summary>
public sealed class SubscriptionConnectionStore : ISubscriptionCredentialSource
{
    private readonly string _directory;
    private readonly SubscriptionOAuth _oauth;
    public SubscriptionConnectionStore(string directory, SubscriptionOAuth oauth)
    {
        _directory = Path.GetFullPath(directory);
        _oauth = oauth;
        Directory.CreateDirectory(_directory);
        RejectLink(_directory);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(_directory,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    public async Task SaveAsync(string id, string name, SubscriptionCredential credential, CancellationToken ct = default)
    {
        ValidateId(id);
        if (string.IsNullOrWhiteSpace(name) || name.Length > 120) throw new ArgumentException("Connection name must be 1–120 characters.");
        await using var lease = await LockAsync(id, ct).ConfigureAwait(false);
        await WriteAsync(id, name, credential, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<SubscriptionConnectionInfo>> ListAsync(CancellationToken ct = default)
    {
        var result = new List<SubscriptionConnectionInfo>();
        foreach (var path in Directory.EnumerateFiles(_directory, "*.json").Order(StringComparer.Ordinal))
        {
            var id = Path.GetFileNameWithoutExtension(path);
            ValidateId(id);
            await using var lease = await LockAsync(id, ct).ConfigureAwait(false);
            if (!File.Exists(path)) continue;
            var (name, credential) = await ReadAsync(id, ct).ConfigureAwait(false);
            result.Add(new(id, name, credential.Provider, credential.AccountId, credential.RefreshAfter));
        }
        return result;
    }

    public async Task DeleteAsync(string id, CancellationToken ct = default)
    {
        ValidateId(id);
        await using var lease = await LockAsync(id, ct).ConfigureAwait(false);
        var path = PathFor(id);
        RejectLink(path);
        File.Delete(path);
        // Keep the lock inode: unlinking it would let another process acquire a different lock.
    }

    public async Task<SubscriptionCredential> GetAsync(string connectionId, SubscriptionProvider provider, CancellationToken ct)
    {
        ValidateId(connectionId);
        await using var lease = await LockAsync(connectionId, ct).ConfigureAwait(false);
        var (name, credential) = await ReadAsync(connectionId, ct).ConfigureAwait(false);
        if (credential.Provider != provider)
            throw new SubscriptionOAuthException("The connection belongs to a different provider.");
        if (credential.RefreshAfter <= DateTimeOffset.UtcNow.AddSeconds(30))
        {
            credential = await _oauth.RefreshAsync(credential, ct).ConfigureAwait(false);
            // Never publish the refreshed token to a caller until its rotated refresh token is durable.
            await WriteAsync(connectionId, name, credential, ct).ConfigureAwait(false);
        }
        return credential;
    }

    private async Task<(string Name, SubscriptionCredential Credential)> ReadAsync(string id, CancellationToken ct)
    {
        var path = PathFor(id);
        RejectLink(path);
        if (!File.Exists(path)) throw new SubscriptionOAuthException("Subscription connection is missing. Connect the account first.");
        try
        {
            var body = JsonNode.Parse(await File.ReadAllTextAsync(path, ct).ConfigureAwait(false))!.AsObject();
            if (body["version"]?.GetValue<int>() != 1) throw new FormatException();
            var provider = body["provider"]?.GetValue<string>() switch
            {
                "codex" => SubscriptionProvider.Codex, "xai" => SubscriptionProvider.Xai, _ => throw new FormatException(),
            };
            return (Required(body, "name"), new(provider, Required(body, "access"), Required(body, "refresh"),
                DateTimeOffset.FromUnixTimeMilliseconds(body["refreshAfter"]!.GetValue<long>()), body["accountId"]?.GetValue<string>()));
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidOperationException or FormatException
            or ArgumentOutOfRangeException or NullReferenceException)
        {
            throw new SubscriptionOAuthException("Stored subscription connection is invalid. Reconnect the account.");
        }
    }

    private async Task WriteAsync(string id, string name, SubscriptionCredential credential, CancellationToken ct)
    {
        var body = new JsonObject
        {
            ["version"] = 1, ["name"] = name,
            ["provider"] = credential.Provider switch { SubscriptionProvider.Codex => "codex", SubscriptionProvider.Xai => "xai", _ => throw new ArgumentException("Unknown provider.") },
            ["access"] = credential.AccessToken, ["refresh"] = credential.RefreshToken,
            ["refreshAfter"] = credential.RefreshAfter.ToUnixTimeMilliseconds(), ["accountId"] = credential.AccountId,
        };
        var path = PathFor(id);
        RejectLink(path);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var file = OpenPrivate(temporary, FileMode.CreateNew, FileShare.None))
            {
                await file.WriteAsync(Encoding.UTF8.GetBytes(body.ToJsonString()), ct).ConfigureAwait(false);
                file.Flush(flushToDisk: true);
            }
            ct.ThrowIfCancellationRequested();
            File.Move(temporary, path, overwrite: true);
        }
        finally { File.Delete(temporary); }
    }

    private async Task<FileStream> LockAsync(string id, CancellationToken ct)
    {
        var path = Path.Combine(_directory, id + ".lock");
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            RejectLink(_directory);
            RejectLink(path);
            try { return OpenPrivate(path, FileMode.OpenOrCreate, FileShare.None); }
            catch (IOException ex) when ((ex.HResult & 0xffff) is 11 or 32 or 33)
            { await Task.Delay(50, ct).ConfigureAwait(false); }
        }
    }

    private static FileStream OpenPrivate(string path, FileMode mode, FileShare share)
    {
        var options = new FileStreamOptions { Mode = mode, Access = FileAccess.ReadWrite, Share = share, Options = FileOptions.Asynchronous };
        if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        return new FileStream(path, options);
    }

    private string PathFor(string id) => Path.Combine(_directory, id + ".json");
    public static void ValidateId(string id)
    {
        if (id.Length is < 1 or > 80 || id.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_'))
            throw new ArgumentException("Connection IDs must contain only letters, numbers, hyphens, and underscores.");
    }
    private static void RejectLink(string path)
    {
        if ((File.Exists(path) || Directory.Exists(path)) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Subscription storage must not use symbolic links.");
    }
    private static string Required(JsonObject body, string key)
        => body[key]?.GetValue<string>() is { Length: > 0 } value ? value : throw new FormatException();
}
