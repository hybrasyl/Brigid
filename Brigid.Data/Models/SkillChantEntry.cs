namespace Brigid.Data.Models;

/// <summary>
///     A skill name paired with its chant text.
/// </summary>
public sealed class SkillChantEntry
{
    public string Chant { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}