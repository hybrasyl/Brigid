#region
using Brigid.Data.AssetPacks;
#endregion

namespace Brigid.Systems;

/// <summary>
///     The theme choices that the player sees in Options (<c>ThemePicker</c>) and in the launcher: Automatic (the first
///     theme), each theme by its display name, and None (the flat look). A choice is stored in
///     <see cref="ClientSettings.UiTheme" />. Free of UI types, so that the asset-free launcher can use it.
/// </summary>
public static class UiThemeChoices
{
    /// <summary>One entry: the stored value (null for Automatic) and the text that a list shows.</summary>
    public readonly record struct Entry(string? Value, string Label);

    /// <summary>
    ///     The entries for <paramref name="themes" />, in their order: Automatic, each theme by its display name, and None.
    ///     Two themes with the same display name show their <c>pack_id</c> too.
    /// </summary>
    public static List<Entry> EntriesFor(IEnumerable<AssetPackManifest> themes)
    {
        var list = themes.ToList();

        var shared = list.GroupBy(manifest => manifest.DisplayName, StringComparer.OrdinalIgnoreCase)
                         .Where(group => group.Count() > 1)
                         .Select(group => group.Key)
                         .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return
        [
            new Entry(null, "Automatic"),
            .. list.Select(manifest => new Entry(
                manifest.PackId,
                shared.Contains(manifest.DisplayName) ? $"{manifest.DisplayName} ({manifest.PackId})" : manifest.DisplayName)),
            new Entry(AssetPackRegistry.NO_UI_THEME, "None")
        ];
    }

    /// <summary>
    ///     The index of the entry with the stored value (case ignored). A value that no entry has, or no value, gives the
    ///     first entry.
    /// </summary>
    public static int IndexOf(IReadOnlyList<Entry> entries, string? stored)
    {
        if (string.IsNullOrWhiteSpace(stored))
            return 0;

        for (var i = 0; i < entries.Count; i++)
            if (string.Equals(entries[i].Value, stored.Trim(), StringComparison.OrdinalIgnoreCase))
                return i;

        return 0;
    }
}
