#region
using System.Text.Json;
using SkiaSharp;
#endregion

namespace Brigid.Data.AssetPacks;

/// <summary>How a theme part fills the rectangle it is drawn into.</summary>
public enum UiThemeFit
{
    /// <summary>Scale to the rectangle on both axes. Nine-slice parts always use this.</summary>
    Stretch,

    /// <summary>Scale to cover the rectangle, keep the aspect ratio, and crop the overflow equally on both sides.</summary>
    Cover,

    /// <summary>Scale to fit inside the rectangle, keep the aspect ratio, and centre the result.</summary>
    Contain
}

/// <summary>How a theme part is sampled where it is enlarged.</summary>
public enum UiThemeFilter
{
    /// <summary>Smooth. Correct for high-resolution art, which is mostly reduced.</summary>
    Linear,

    /// <summary>Hard pixel edges. Correct for pixel art drawn at 1× density and enlarged.</summary>
    Nearest
}

/// <summary>
///     One art part of a theme: the archive entry that holds the image and how to fit it. <see cref="Slice" /> is the
///     nine-slice inset in art pixels (left, top, right, bottom), or null for a part that scales as one image. When
///     <see cref="Tile" /> is true, the edges and the centre of a nine-slice part repeat instead of stretching.
///     <see cref="Filter" /> is null when the part uses the theme's filter.
/// </summary>
public sealed record UiThemePart(string File, SKRectI? Slice, UiThemeFit Fit, bool Tile, UiThemeFilter? Filter = null);

/// <summary>
///     The parsed <c>theme.json</c> of a <c>ui_theme</c> pack. Parts, colours and fonts are keyed by role (for example
///     <c>button.normal</c> or <c>text.disabled</c>), not by panel, so every themed primitive with the same role gets the
///     same look. Role lookup ignores case.
/// </summary>
public sealed class UiTheme
{
    /// <summary>The default number of art pixels for each logical (640×480) pixel.</summary>
    public const float DEFAULT_DENSITY = 1f;

    /// <summary>The lowest and highest density that a theme can declare.</summary>
    public const float MIN_DENSITY = 0.25f;

    public const float MAX_DENSITY = 16f;

    /// <summary>The largest nine-slice inset, in art pixels. Larger values are not real art and would overflow the maths.</summary>
    public const int MAX_SLICE = 16384;

    /// <summary>A theme with no parts, colours or fonts. Every role falls back to the flat look.</summary>
    public static UiTheme Empty { get; } = new(DEFAULT_DENSITY, UiThemeFilter.Linear, new(), new(), new());

    /// <summary>Art pixels for each logical pixel. A part drawn at 4× density has 4 art pixels for each logical pixel.</summary>
    public float Density { get; }

    /// <summary>The filter for a part that does not set its own.</summary>
    public UiThemeFilter Filter { get; }

    public IReadOnlyDictionary<string, UiThemePart> Parts { get; }

    public IReadOnlyDictionary<string, SKColor> Colors { get; }

    /// <summary>Font archive entries by role, for example <c>title</c>.</summary>
    public IReadOnlyDictionary<string, string> Fonts { get; }

    private UiTheme(
        float density,
        UiThemeFilter filter,
        Dictionary<string, UiThemePart> parts,
        Dictionary<string, SKColor> colors,
        Dictionary<string, string> fonts)
    {
        Density = density;
        Filter = filter;
        Parts = parts;
        Colors = colors;
        Fonts = fonts;
    }

    /// <summary>
    ///     Parses <c>theme.json</c>. Returns null when the text is not a JSON object. A part, colour or font entry that is
    ///     not valid is skipped and reported through <paramref name="warn" />, so one bad entry does not disable the
    ///     theme. A key that the client does not know is ignored, so a newer theme still loads.
    /// </summary>
    public static UiTheme? Parse(string json, Action<string>? warn = null)
    {
        JsonDocument document;

        try
        {
            document = JsonDocument.Parse(json);
        } catch (JsonException ex)
        {
            warn?.Invoke($"theme.json is not valid JSON: {ex.Message}");

            return null;
        }

        using (document)
        {
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                warn?.Invoke("theme.json is not a JSON object");

                return null;
            }

            var density = DEFAULT_DENSITY;

            if (root.TryGetProperty("density", out var densityElement))
            {
                if ((densityElement.ValueKind == JsonValueKind.Number)
                    && densityElement.TryGetSingle(out var value)
                    && float.IsFinite(value)
                    && (value >= MIN_DENSITY)
                    && (value <= MAX_DENSITY))
                    density = value;
                else
                    warn?.Invoke($"density must be a number from {MIN_DENSITY} to {MAX_DENSITY}; using {DEFAULT_DENSITY}");
            }

            var filter = UiThemeFilter.Linear;

            if (root.TryGetProperty("filter", out var filterElement))
            {
                if (TryParseFilter(filterElement, out var parsed))
                    filter = parsed;
                else
                    warn?.Invoke("filter must be linear or nearest; using linear");
            }

            return new UiTheme(density, filter, ParseParts(root, warn), ParseColors(root, warn), ParseFonts(root, warn));
        }
    }

    private static Dictionary<string, UiThemePart> ParseParts(JsonElement root, Action<string>? warn)
    {
        var parts = new Dictionary<string, UiThemePart>(StringComparer.OrdinalIgnoreCase);

        if (!TryGetObject(root, "parts", warn, out var partsElement))
            return parts;

        foreach (var property in partsElement.EnumerateObject())
            if (TryParsePart(property.Name, property.Value, warn, out var part, out var problem))
                parts[property.Name] = part;
            else
                warn?.Invoke($"part '{property.Name}' skipped: {problem}");

        return parts;
    }

    private static bool TryParsePart(string role, JsonElement element, Action<string>? warn, out UiThemePart part, out string problem)
    {
        part = null!;

        if (element.ValueKind != JsonValueKind.Object)
        {
            problem = "not an object";

            return false;
        }

        if (!element.TryGetProperty("file", out var fileElement)
            || (fileElement.ValueKind != JsonValueKind.String)
            || string.IsNullOrWhiteSpace(fileElement.GetString()))
        {
            problem = "no file";

            return false;
        }

        SKRectI? slice = null;

        if (element.TryGetProperty("slice", out var sliceElement))
        {
            if (!TryParseSlice(sliceElement, out var parsed))
            {
                problem = $"slice must be four whole numbers from 0 to {MAX_SLICE}: [left, top, right, bottom]";

                return false;
            }

            slice = parsed;
        }

        //an unknown fit or filter keeps the part with the default, so a newer value still shows art
        var fit = UiThemeFit.Stretch;

        if (element.TryGetProperty("fit", out var fitElement) && !TryParseFit(fitElement, out fit))
        {
            fit = UiThemeFit.Stretch;
            warn?.Invoke($"part '{role}': fit must be stretch, cover or contain; using stretch");
        }

        UiThemeFilter? filter = null;

        if (element.TryGetProperty("filter", out var filterElement))
        {
            if (TryParseFilter(filterElement, out var parsedFilter))
                filter = parsedFilter;
            else
                warn?.Invoke($"part '{role}': filter must be linear or nearest; using the theme filter");
        }

        var tile = element.TryGetProperty("tile", out var tileElement) && (tileElement.ValueKind == JsonValueKind.True);

        part = new UiThemePart(fileElement.GetString()!, slice, fit, tile, filter);
        problem = string.Empty;

        return true;
    }

    private static bool TryParseFit(JsonElement element, out UiThemeFit fit)
    {
        fit = UiThemeFit.Stretch;
        var text = element.ValueKind == JsonValueKind.String ? element.GetString() : null;

        return text is not null && Enum.TryParse(text, true, out fit) && Enum.IsDefined(fit) && !int.TryParse(text, out _);
    }

    private static bool TryParseFilter(JsonElement element, out UiThemeFilter filter)
    {
        filter = UiThemeFilter.Linear;
        var text = element.ValueKind == JsonValueKind.String ? element.GetString() : null;

        return text is not null && Enum.TryParse(text, true, out filter) && Enum.IsDefined(filter) && !int.TryParse(text, out _);
    }

    private static bool TryParseSlice(JsonElement element, out SKRectI slice)
    {
        slice = default;

        if ((element.ValueKind != JsonValueKind.Array) || (element.GetArrayLength() != 4))
            return false;

        Span<int> values = stackalloc int[4];
        var i = 0;

        foreach (var item in element.EnumerateArray())
        {
            if ((item.ValueKind != JsonValueKind.Number) || !item.TryGetInt32(out var value) || (value < 0) || (value > MAX_SLICE))
                return false;

            values[i++] = value;
        }

        slice = new SKRectI(values[0], values[1], values[2], values[3]);

        return true;
    }

    private static Dictionary<string, SKColor> ParseColors(JsonElement root, Action<string>? warn)
    {
        var colors = new Dictionary<string, SKColor>(StringComparer.OrdinalIgnoreCase);

        if (!TryGetObject(root, "colors", warn, out var colorsElement))
            return colors;

        foreach (var property in colorsElement.EnumerateObject())
        {
            var text = property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() : null;

            //only the two long forms: SKColor.TryParse also takes #RGB and #ARGB, which the format does not define
            if (text is { Length: 7 or 9 } && text.StartsWith('#') && SKColor.TryParse(text, out var color))
                colors[property.Name] = color;
            else
                warn?.Invoke($"color '{property.Name}' skipped: use #RRGGBB or #AARRGGBB");
        }

        return colors;
    }

    private static Dictionary<string, string> ParseFonts(JsonElement root, Action<string>? warn)
    {
        var fonts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (!TryGetObject(root, "fonts", warn, out var fontsElement))
            return fonts;

        foreach (var property in fontsElement.EnumerateObject())
        {
            var file = property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() : null;

            if (!string.IsNullOrWhiteSpace(file))
                fonts[property.Name] = file;
            else
                warn?.Invoke($"font '{property.Name}' skipped: no file");
        }

        return fonts;
    }

    private static bool TryGetObject(JsonElement root, string name, Action<string>? warn, out JsonElement element)
    {
        if (!root.TryGetProperty(name, out element))
            return false;

        if (element.ValueKind == JsonValueKind.Object)
            return true;

        warn?.Invoke($"{name} must be an object; ignored");

        return false;
    }
}
