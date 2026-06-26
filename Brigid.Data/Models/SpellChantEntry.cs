namespace Brigid.Data.Models;

/// <summary>
///     A spell name paired with its 10 sequential chant lines.
/// </summary>
public sealed class SpellChantEntry
{
    public string[] Chants { get; set; } = new string[10];
    public string Name { get; set; } = string.Empty;

    public SpellChantEntry()
    {
        for (var i = 0; i < 10; i++)
            Chants[i] = string.Empty;
    }
}