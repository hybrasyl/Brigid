#region
using System.IO.Compression;
using System.Text;
using Brigid.Data.AssetPacks;
using SkiaSharp;
using Xunit;
#endregion

namespace Brigid.Tests;

/// <summary>
///     The <c>theme.json</c> of a <c>ui_theme</c> pack, and the pack lookups built on it. One bad entry must not disable
///     the theme: it is skipped and reported, and every other role still works.
/// </summary>
public class UiThemeTests
{
    [Fact]
    public void Parse_ReadsPartsColorsFontsAndDensity()
    {
        var theme = UiTheme.Parse(
            """
            {
              "density": 4,
              "parts": {
                "frame.panel": { "file": "frame.png", "slice": [96, 80, 96, 80], "tile": true },
                "background.mainmenu": { "file": "bg.webp", "fit": "cover" }
              },
              "colors": { "text": "#E8E4D8", "text.disabled": "#807A7A80" },
              "fonts": { "title": "title.ttf" }
            }
            """);

        Assert.NotNull(theme);
        Assert.Equal(4f, theme.Density);

        var frame = theme.Parts["frame.panel"];
        Assert.Equal("frame.png", frame.File);
        Assert.Equal(new SKRectI(96, 80, 96, 80), frame.Slice);
        Assert.Equal(UiThemeFit.Stretch, frame.Fit);
        Assert.True(frame.Tile);

        var background = theme.Parts["background.mainmenu"];
        Assert.Null(background.Slice);
        Assert.Equal(UiThemeFit.Cover, background.Fit);
        Assert.False(background.Tile);

        Assert.Equal(new SKColor(0xE8, 0xE4, 0xD8), theme.Colors["text"]);
        Assert.Equal(new SKColor(0x7A, 0x7A, 0x80, 0x80), theme.Colors["text.disabled"]);
        Assert.Equal("title.ttf", theme.Fonts["title"]);
    }

    [Fact]
    public void Parse_RolesIgnoreCase()
    {
        var theme = UiTheme.Parse("""{ "parts": { "Button.Normal": { "file": "b.png" } }, "colors": { "TEXT": "#FFFFFF" } }""");

        Assert.NotNull(theme);
        Assert.True(theme.Parts.ContainsKey("button.normal"));
        Assert.True(theme.Colors.ContainsKey("text"));
    }

    [Fact]
    public void Parse_SkipsABadEntryAndKeepsTheRest()
    {
        var warnings = new List<string>();

        var theme = UiTheme.Parse(
            """
            {
              "parts": {
                "good": { "file": "good.png" },
                "no.file": { "slice": [1, 1, 1, 1] },
                "short.slice": { "file": "a.png", "slice": [1, 2, 3] },
                "negative.slice": { "file": "a.png", "slice": [1, -2, 3, 4] },
                "huge.slice": { "file": "a.png", "slice": [1, 2, 3, 2147483647] }
              },
              "colors": { "good": "#102030", "named": "red", "short": "#12", "rgb": "#FFF", "argb": "#FFFF" },
              "fonts": { "good": "a.ttf", "empty": "" }
            }
            """,
            warnings.Add);

        Assert.NotNull(theme);
        Assert.Equal(["good"], theme.Parts.Keys);
        Assert.Equal(["good"], theme.Colors.Keys);
        Assert.Equal(["good"], theme.Fonts.Keys);
        //four parts, four colours and one font
        Assert.Equal(9, warnings.Count);
    }

    [Theory]
    [InlineData("zoom")]
    [InlineData("1")]
    public void Parse_KeepsAPartWithAnUnknownFitAndStretchesIt(string fit)
    {
        var warnings = new List<string>();

        var theme = UiTheme.Parse($$"""{ "parts": { "bg": { "file": "bg.png", "fit": "{{fit}}" } } }""", warnings.Add);

        Assert.Equal(UiThemeFit.Stretch, theme!.Parts["bg"].Fit);
        Assert.Single(warnings);
    }

    [Fact]
    public void Parse_ReadsTheThemeFilterAndAPartOverride()
    {
        var theme = UiTheme.Parse(
            """
            {
              "filter": "nearest",
              "parts": {
                "inherits": { "file": "a.png" },
                "smooth": { "file": "b.png", "filter": "linear" },
                "unknown": { "file": "c.png", "filter": "bicubic" }
              }
            }
            """);

        Assert.Equal(UiThemeFilter.Nearest, theme!.Filter);
        Assert.Null(theme.Parts["inherits"].Filter);
        Assert.Equal(UiThemeFilter.Linear, theme.Parts["smooth"].Filter);
        Assert.Null(theme.Parts["unknown"].Filter);
    }

    [Fact]
    public void Parse_UsesLinearWhenTheFilterIsNotKnown()
        => Assert.Equal(UiThemeFilter.Linear, UiTheme.Parse("""{ "filter": 2 }""")!.Filter);

    [Theory]
    [InlineData("""{ }""")]
    [InlineData("""{ "density": 0 }""")]
    [InlineData("""{ "density": -2 }""")]
    [InlineData("""{ "density": "4" }""")]
    //a value that overflows a float, and values outside the allowed range
    [InlineData("""{ "density": 1e39 }""")]
    [InlineData("""{ "density": 1e-30 }""")]
    [InlineData("""{ "density": 0.1 }""")]
    [InlineData("""{ "density": 17 }""")]
    public void Parse_UsesTheDefaultDensityWhenItIsAbsentOrNotValid(string json)
        => Assert.Equal(UiTheme.DEFAULT_DENSITY, UiTheme.Parse(json)!.Density);

    [Theory]
    [InlineData("not json")]
    [InlineData("[1, 2]")]
    [InlineData("\"text\"")]
    public void Parse_ReturnsNullForTextThatIsNotAJsonObject(string json)
    {
        var warnings = new List<string>();

        Assert.Null(UiTheme.Parse(json, warnings.Add));
        Assert.Single(warnings);
    }

    [Fact]
    public void Parse_IgnoresASectionThatIsNotAnObject()
    {
        var warnings = new List<string>();

        var theme = UiTheme.Parse("""{ "parts": [], "colors": 3 }""", warnings.Add);

        Assert.NotNull(theme);
        Assert.Empty(theme.Parts);
        Assert.Empty(theme.Colors);
        Assert.Equal(2, warnings.Count);
    }

    [Fact]
    public void Pack_GetsAPartImageAndAFont()
    {
        using var pack = BuildPack(
            """{ "parts": { "button.normal": { "file": "button.png" } }, "fonts": { "title": "title.ttf" } }""",
            ("button.png", Png(6, 4)),
            ("title.ttf", [1, 2, 3]));

        Assert.True(pack.TryGetPartImage("BUTTON.NORMAL", out var part, out var image));
        Assert.Equal("button.png", part!.File);
        Assert.Equal(6, image!.Width);
        Assert.Equal(4, image.Height);
        image.Dispose();

        Assert.True(pack.TryGetFontBytes("title", out var font));
        Assert.Equal([1, 2, 3], font);
    }

    [Fact]
    public void Pack_TreatsAMissingOrBrokenEntryAsNoPart()
    {
        using var pack = BuildPack(
            """{ "parts": { "missing": { "file": "missing.png" }, "broken": { "file": "broken.png" } } }""",
            ("broken.png", [0x89, 0x50, 0x4E, 0x47]));

        Assert.False(pack.TryGetPartImage("missing", out var missing, out _));
        Assert.Null(missing);
        Assert.False(pack.TryGetPartImage("broken", out _, out _));
        Assert.False(pack.TryGetPartImage("not.a.role", out _, out _));
        Assert.False(pack.TryGetFontBytes("title", out _));
    }

    [Fact]
    public void Pack_WithNoThemeFileHasAnEmptyTheme()
    {
        using var pack = BuildPack(null);

        Assert.Same(UiTheme.Empty, pack.Theme);
        Assert.Empty(pack.Theme.Parts);
        Assert.Equal(UiTheme.DEFAULT_DENSITY, pack.Theme.Density);
    }

    private static UiThemePack BuildPack(string? themeJson, params (string Name, byte[] Bytes)[] entries)
    {
        var stream = new MemoryStream();

        using (var writer = new ZipArchive(stream, ZipArchiveMode.Create, true))
        {
            if (themeJson is not null)
                Write(writer, UiThemePack.THEME_ENTRY_NAME, Encoding.UTF8.GetBytes(themeJson));

            foreach (var (name, bytes) in entries)
                Write(writer, name, bytes);
        }

        stream.Position = 0;

        var manifest = new AssetPackManifest { SchemaVersion = 1, PackId = "test-theme", ContentType = "ui_theme" };

        return new UiThemePack(new ZipArchive(stream, ZipArchiveMode.Read), manifest);
    }

    private static void Write(ZipArchive archive, string name, byte[] bytes)
    {
        using var entry = archive.CreateEntry(name).Open();
        entry.Write(bytes);
    }

    private static byte[] Png(int width, int height)
    {
        using var bitmap = new SKBitmap(width, height);
        bitmap.Erase(SKColors.Teal);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);

        return data.ToArray();
    }
}
