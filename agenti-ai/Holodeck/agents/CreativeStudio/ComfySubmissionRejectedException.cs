namespace CreativeStudio;

// A validation rejection is known not to have entered ComfyUI's execution queue.
public sealed class ComfySubmissionRejectedException(string message) : IOException(message)
{
}
