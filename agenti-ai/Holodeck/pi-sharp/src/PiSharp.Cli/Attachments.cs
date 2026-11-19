// Ported from pi: packages/coding-agent/src/cli/file-processor.ts @ 71dca871b
// No image resize (decision 4).
using PiSharp.Core.Messages;
using PiSharp.Tools;

namespace PiSharp.Cli;

public sealed record ProcessedFiles(string Text, IReadOnlyList<ImageContent> Images);

/// <summary>Turns <c>@file</c> CLI arguments into text wrappers and image attachments.</summary>
public static class Attachments
{
    public static ProcessedFiles Process(
        IReadOnlyList<string> fileArgs,
        string cwd,
        int contextWindow = 0,
        int warnPixels = ImageWarnings.DefaultWarnPixels,
        Action<string>? warningSink = null,
        bool supportsImages = true)
    {
        var text = new System.Text.StringBuilder();
        var images = new List<ImageContent>();
        var errors = new List<string>();

        foreach (var fileArg in fileArgs)
        {
            var absolute = Path.GetFullPath(ToolPaths.ExpandPath(fileArg), cwd);
            if (!File.Exists(absolute))
            {
                errors.Add($"File not found: {absolute}");
                continue;
            }
            var bytes = File.ReadAllBytes(absolute);
            if (bytes.Length == 0)
            {
                if (ImageValidation.IsImageFileName(absolute)) ImageValidation.RequireValid(bytes, "image/png", absolute);
                continue;
            }
            var mime = ImageSniffer.Detect(bytes);
            if (mime is not null)
            {
                ImageValidation.RequireValid(bytes, mime, absolute);
                images.Add(new ImageContent(Convert.ToBase64String(bytes), mime));
                text.Append("<file name=\"").Append(absolute.Replace('\\', '/')).Append("\"></file>\n");
                // adr-pi-sharp-image-size-warning: the size warning tells the model to crop or
                // downscale before reading. For a model that cannot take images
                // (SupportsImages=false) that advice is moot, so the warning is skipped; the image
                // itself is still attached exactly as before.
                var warning = supportsImages
                    ? ImageWarnings.For(bytes, warnPixels, contextWindow)
                    : null;
                if (warning is not null)
                {
                    text.Append(warning).Append('\n');
                    warningSink?.Invoke(warning);
                }
                continue;
            }

            try
            {
                var content = File.ReadAllText(absolute);
                if (content.StartsWith('\uFEFF')) content = content[1..];
                text.Append("<file name=\"").Append(absolute.Replace('\\', '/')).Append("\">\n")
                    .Append(content).Append("\n</file>\n");
            }
            catch (Exception ex)
            {
                errors.Add($"Could not read file {absolute}: {ex.Message}");
            }
        }

        if (errors.Count > 0)
            throw new FileNotFoundException(string.Join('\n', errors));

        return new ProcessedFiles(text.ToString(), images);
    }

    public static UserMessage ToUserMessage(string prompt, ProcessedFiles? files)
    {
        var parts = new List<ContentPart>();
        var text = prompt ?? "";
        if (files is not null && files.Text.Length > 0)
            text = files.Text + (text.Length > 0 ? "\n" + text : "");
        if (text.Length > 0) parts.Add(new TextContent(text));
        if (files is not null) parts.AddRange(files.Images);
        if (parts.Count == 0) parts.Add(new TextContent(""));
        return new UserMessage(parts);
    }
}
