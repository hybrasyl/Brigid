#region
using System.IO.Compression;
using System.Text;
using Brigid.Controls.Generic;
using Brigid.Data.AssetPacks;
using Brigid.Systems;
using Xunit;
#endregion

namespace Brigid.Tests;

/// <summary>
///     The theme choice in the launcher (BRIG-124): before the assets load, the launcher lists the themes from a scan of
///     the pack manifests, in the order that registration gives, and reads the saved choice from <c>brigid.cfg</c>
///     without loading the settings. A saved value selects its entry; an unknown value selects Automatic.
/// </summary>
public sealed class LauncherThemeTests : IDisposable
{
    private readonly string Root = Path.Combine(Path.GetTempPath(), "brigid-themes-" + Guid.NewGuid().ToString("N"));

    public LauncherThemeTests() => Directory.CreateDirectory(Root);

    public void Dispose() => Directory.Delete(Root, true);

    private void WritePack(string fileName, string? manifestJson)
    {
        using var zip = ZipFile.Open(Path.Combine(Root, fileName), ZipArchiveMode.Create);

        if (manifestJson is null)
            return;

        using var stream = zip.CreateEntry("_manifest.json").Open();
        stream.Write(Encoding.UTF8.GetBytes(manifestJson));
    }

    private static string Manifest(string contentType, string packId, int priority = 100, string? name = null)
        => $$"""{"schema_version": 1, "pack_id": "{{packId}}", "content_type": "{{contentType}}", "priority": {{priority}}{{(name is null ? "" : $", \"name\": \"{name}\"")}}}""";

    private string[] Scanned() => AssetPackRegistry.ScanUiThemes(Root).Select(manifest => manifest.PackId).ToArray();

    [Fact]
    public void ScanListsTheThemesByPriorityThenFileName()
    {
        WritePack("a-wood.datf", Manifest("ui_theme", "wood"));
        WritePack("b-stone.datf", Manifest("ui_theme", "stone", 200));
        WritePack("c-ice.datf", Manifest("ui_theme", "ice"));
        WritePack("d-items.datf", Manifest("item_icons", "items"));

        Assert.Equal(["stone", "wood", "ice"], Scanned());
    }

    [Fact]
    public void ScanSkipsThePacksThatRegistrationRefuses()
    {
        WritePack("a-items.datf", Manifest("item_icons", "shared"));
        WritePack("b-copy.datf", Manifest("ui_theme", "shared"));
        WritePack("c-none.datf", Manifest("ui_theme", AssetPackRegistry.NO_UI_THEME));
        WritePack("d-unversioned.datf", """{"pack_id": "old", "content_type": "ui_theme"}""");
        WritePack("e-no-manifest.datf", null);
        WritePack("f-bad-json.datf", "{");
        File.WriteAllBytes(Path.Combine(Root, "g-not-a-zip.datf"), [1, 2, 3]);
        WritePack("h-ok.datf", Manifest("ui_theme", "ok"));

        Assert.Equal(["ok"], Scanned());
    }

    [Fact]
    public void ScanLetsAPackOfAnUnknownTypeLeaveItsIdFree()
    {
        WritePack("a-future.datf", Manifest("future_type", "stone"));
        WritePack("b-stone.datf", Manifest("ui_theme", "stone"));

        Assert.Equal(["stone"], Scanned());
    }

    [Fact]
    public void ScanOfAnAbsentFolderIsEmpty() => Assert.Empty(AssetPackRegistry.ScanUiThemes(Path.Combine(Root, "absent")));

    [Fact]
    public void ScannedThemesGiveTheEntriesByDisplayName()
    {
        WritePack("a-wood.datf", Manifest("ui_theme", "wood", name: "Wood"));
        WritePack("b-stone.datf", Manifest("ui_theme", "stone"));

        Assert.Equal(["Automatic", "Wood", "stone", "None"], ThemePicker.EntriesFor(AssetPackRegistry.ScanUiThemes(Root)).Select(entry => entry.Label));
    }

    [Fact]
    public void IndexOfFindsTheSavedValueAndElseAutomatic()
    {
        ThemePicker.Entry[] entries = [new(null, "Automatic"), new("stone", "Stone"), new(AssetPackRegistry.NO_UI_THEME, "None")];

        Assert.Equal(1, ThemePicker.IndexOf(entries, " STONE "));
        Assert.Equal(2, ThemePicker.IndexOf(entries, "none"));
        Assert.Equal(0, ThemePicker.IndexOf(entries, "wood"));
        Assert.Equal(0, ThemePicker.IndexOf(entries, null));
    }

    [Fact]
    public void ReadSavedUiThemeReadsOnlyTheThemeLine()
    {
        var path = Path.Combine(Root, "brigid.cfg");

        File.WriteAllLines(path, ["Sound Volume: 7", "UiTheme: stone", "Music Volume: 3"]);
        Assert.Equal("stone", ClientSettings.ReadSavedUiTheme(path));

        File.WriteAllLines(path, ["Sound Volume: 7", "UiTheme: "]);
        Assert.Null(ClientSettings.ReadSavedUiTheme(path));

        File.WriteAllLines(path, ["Sound Volume: 7"]);
        Assert.Null(ClientSettings.ReadSavedUiTheme(path));

        Assert.Null(ClientSettings.ReadSavedUiTheme(Path.Combine(Root, "absent.cfg")));
    }
}
