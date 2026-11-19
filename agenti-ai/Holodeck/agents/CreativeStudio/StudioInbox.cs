namespace CreativeStudio;

// Keep a fetched message in memory until its durable receipt is confirmed. Redelivered handled packets need no model work.
public sealed class StudioInbox(HolodeckClient transport, CreativeStore store, int pollSeconds)
{
  private IncomingMessage? held;

  public async Task<StoredReceipt?> NextAsync(CancellationToken token)
  {
    StoredReceipt? pending = await store.PendingReceiptAsync(token);
    if (pending is not null)
    {
      if (held is not null && held.PacketId == pending.PacketId)
      {
        held = null;
      }

      return pending;
    }

    held ??= await transport.NextAsync(pollSeconds, token);
    if (held is null)
    {
      return null;
    }

    StoredReceipt receipt = new(held.PacketId, held.ChannelId, held.SenderId, held.From, held.Note, held.Files);
    await store.ReceiveAsync(receipt, token);
    held = null;
    return await store.PendingReceiptAsync(token);
  }
}
