namespace Brigid.Utilities;

/// <summary>
///     Cross-platform system clipboard access via TextCopy.
/// </summary>
public static class Clipboard
{
    public static string GetText()
    {
        try
        {
            return TextCopy.ClipboardService.GetText() ?? string.Empty;
        } catch
        {
            return string.Empty;
        }
    }

    public static void SetText(string text)
    {
        try
        {
            TextCopy.ClipboardService.SetText(text);
        } catch
        {
            //clipboard unavailable — silently ignore
        }
    }
}
