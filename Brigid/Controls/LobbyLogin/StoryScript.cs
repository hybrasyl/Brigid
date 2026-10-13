#region
using System.Text.Json;
#endregion

namespace Brigid.Controls.LobbyLogin;

/// <summary>The kind of a <see cref="StoryPage" />.</summary>
public enum StoryPageKind
{
    /// <summary>An image that fills the window, with the text in a caption box.</summary>
    Slide,

    /// <summary>Markdown text in a framed box.</summary>
    Text,

    /// <summary>A video. Until cutscene playback exists (BRIG-26), a placeholder frame with the text.</summary>
    Cutscene
}

/// <summary>
///     One page of the story. <paramref name="Image" /> names a <c>ui_theme</c> part (a slide), and
///     <paramref name="Video" /> names a cutscene. <paramref name="Text" /> is markdown.
/// </summary>
public sealed record StoryPage(StoryPageKind Kind, string Text, string? Title = null, string? Image = null, string? Video = null);

/// <summary>
///     The story that the main menu's Story button shows, from the <c>story.json</c> that ships in the client. The file
///     lists the pages in order:
///     <code>
///     { "pages": [ { "kind": "slide", "image": "story.01", "text": "..." }, { "kind": "text", "title": "...", "text": "..." } ] }
///     </code>
/// </summary>
public sealed class StoryScript
{
    public static readonly StoryScript Empty = new([]);

    public IReadOnlyList<StoryPage> Pages { get; }

    public StoryScript(IReadOnlyList<StoryPage> pages) => Pages = pages;

    /// <summary>
    ///     Parses <c>story.json</c>. Text that is not a JSON object with a <c>pages</c> array gives an empty story. A page
    ///     that is not valid is skipped and reported through <paramref name="warn" />, so one bad page does not remove the
    ///     story. A key that the client does not know is ignored. The file is written by hand, so a trailing comma and a
    ///     comment are accepted.
    /// </summary>
    public static StoryScript Parse(string json, Action<string>? warn = null)
    {
        JsonDocument document;

        try
        {
            document = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
        } catch (JsonException ex)
        {
            warn?.Invoke($"story.json is not valid JSON: {ex.Message}");

            return Empty;
        }

        using (document)
        {
            var root = document.RootElement;

            if ((root.ValueKind != JsonValueKind.Object)
                || !root.TryGetProperty("pages", out var pagesElement)
                || (pagesElement.ValueKind != JsonValueKind.Array))
            {
                warn?.Invoke("story.json must be an object with a \"pages\" array");

                return Empty;
            }

            var pages = new List<StoryPage>();
            var number = 0;

            foreach (var element in pagesElement.EnumerateArray())
            {
                number++;

                if (TryParsePage(element, number, warn, out var page))
                    pages.Add(page);
            }

            return new StoryScript(pages);
        }
    }

    private static bool TryParsePage(JsonElement element, int number, Action<string>? warn, out StoryPage page)
    {
        page = null!;

        if (element.ValueKind != JsonValueKind.Object)
        {
            warn?.Invoke($"story page {number} skipped: it is not an object");

            return false;
        }

        //an exact name only: Enum.TryParse also takes numbers and comma-separated lists
        StoryPageKind? kind = ReadString(element, "kind", number, warn)?.ToLowerInvariant() switch
        {
            "slide" => StoryPageKind.Slide,
            "text" => StoryPageKind.Text,
            "cutscene" => StoryPageKind.Cutscene,
            _ => null
        };

        if (kind is null)
        {
            warn?.Invoke($"story page {number} skipped: kind must be slide, text or cutscene");

            return false;
        }

        var image = ReadString(element, "image", number, warn);

        if ((kind == StoryPageKind.Slide) && string.IsNullOrWhiteSpace(image))
            warn?.Invoke($"story page {number}: a slide has no image; it shows a placeholder");

        page = new StoryPage(
            kind.Value,
            ReadString(element, "text", number, warn) ?? string.Empty,
            ReadString(element, "title", number, warn),
            image,
            ReadString(element, "video", number, warn));

        return true;
    }

    //a key that is present but not a string is reported and read as absent
    private static string? ReadString(JsonElement element, string key, int number, Action<string>? warn)
    {
        if (!element.TryGetProperty(key, out var value) || (value.ValueKind == JsonValueKind.Null))
            return null;

        if (value.ValueKind == JsonValueKind.String)
            return value.GetString();

        warn?.Invoke($"story page {number}: {key} must be a string");

        return null;
    }
}
