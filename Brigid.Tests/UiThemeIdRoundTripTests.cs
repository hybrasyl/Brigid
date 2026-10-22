#region
using System.IO.Compression;
using System.Text;
using Brigid.Data.AssetPacks;
using Brigid.Systems;
using Xunit;
#endregion

namespace Brigid.Tests;

/// <summary>
///     A theme id that registration accepts comes back the same from <c>brigid.cfg</c>: the saved choice selects the same
///     theme after a restart (PR #64 review).
/// </summary>
[Collection("AssetPackRegistry")]
public sealed class UiThemeIdRoundTripTests : IDisposable
{
    private readonly string Root = Path.Combine(Path.GetTempPath(), "brigid-theme-id-" + Guid.NewGuid().ToString("N"));
    private readonly string? SavedUiTheme = ClientSettings.UiTheme;

    public UiThemeIdRoundTripTests() => AssetPackRegistry.ResetForTest();

    public void Dispose()
    {
        ClientSettings.UiTheme = SavedUiTheme;
        AssetPackRegistry.ResetForTest();

        if (Directory.Exists(Root))
            Directory.Delete(Root, true);
    }

    private static UiThemePack RegisterTheme(string packId)
    {
        var stream = new MemoryStream();

        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true))
        using (var entry = zip.CreateEntry(UiThemePack.THEME_ENTRY_NAME).Open())
            entry.Write(Encoding.UTF8.GetBytes("{}"));

        stream.Position = 0;

        var manifest = new AssetPackManifest { SchemaVersion = 1, PackId = packId, ContentType = "ui_theme" };

        Assert.Null(AssetPackRegistry.GetManifestRejection(manifest));
        Assert.True(AssetPackRegistry.RegisterForTest(new ZipArchive(stream, ZipArchiveMode.Read), manifest));

        return AssetPackRegistry.UiThemePacks.Single(pack => pack.Manifest.PackId == packId);
    }

    [Theory]
    [InlineData("stone")]
    [InlineData("Icy-Steel_2")]
    [InlineData("7")]
    [InlineData("-_-")]
    public void ASavedThemeIdSelectsTheSameThemeAfterARestart(string packId)
    {
        RegisterTheme("first");
        var theme = RegisterTheme(packId);
        var path = Path.Combine(Root, "brigid.cfg");

        ClientSettings.UiTheme = packId;
        ClientSettings.Save(path);

        var saved = ClientSettings.ReadSavedUiTheme(path);

        Assert.Equal(packId, saved);
        Assert.Same(theme, AssetPackRegistry.ResolveUiTheme(saved));
    }
}
