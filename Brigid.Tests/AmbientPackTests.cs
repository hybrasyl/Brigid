#region
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Brigid.Data.AssetPacks;
using Xunit;
#endregion

namespace Brigid.Tests;

/// <summary>
///     The <c>ambient_sounds</c> pack type. The manifest JSON in these tests has the shape that Taliesin writes:
///     <c>covers.ambient_sounds</c> is keyed by ambient id, and only a one-shot has an entry.
/// </summary>
public class AmbientPackTests
{
    private static AmbientPack BuildPack(string coversJson, params string[] entryNames)
    {
        var manifestJson = $$"""
                             {
                               "schema_version": 1,
                               "pack_id": "test-ambient",
                               "content_type": "ambient_sounds",
                               "covers": {{coversJson}}
                             }
                             """;

        var manifest = JsonSerializer.Deserialize<AssetPackManifest>(manifestJson)!;

        var stream = new MemoryStream();

        using (var writer = new ZipArchive(stream, ZipArchiveMode.Create, true))
            foreach (var name in entryNames)
            {
                using var entry = writer.CreateEntry(name).Open();
                entry.Write(Encoding.ASCII.GetBytes(name));
            }

        stream.Position = 0;

        return new AmbientPack(new ZipArchive(stream, ZipArchiveMode.Read), manifest);
    }

    [Fact]
    public void Loops_IsTrue_WhenTheManifestHasNoEntryForTheId()
    {
        using var pack = BuildPack("""{ "ambient_sounds": {} }""", "amb_0001.wav");

        Assert.True(pack.Loops(1));
    }

    [Fact]
    public void Loops_IsTrue_WhenTheManifestHasNoAmbientCovers()
    {
        using var pack = BuildPack("{}", "amb_0001.wav");

        Assert.True(pack.Loops(1));
    }

    [Fact]
    public void Loops_IsFalse_OnlyForAnExplicitFalse()
    {
        using var pack = BuildPack("""
                                   {
                                     "ambient_sounds": {
                                       "1": { "loop": false },
                                       "2": { "loop": true },
                                       "3": {},
                                       "4": { "loop": "no" },
                                       "5": false
                                     }
                                   }
                                   """);

        Assert.False(pack.Loops(1));
        Assert.True(pack.Loops(2));
        Assert.True(pack.Loops(3));
        Assert.True(pack.Loops(4));
        Assert.True(pack.Loops(5));
    }

    [Fact]
    public void Loops_IgnoresAKeyThatIsNotAPlainNumber()
    {
        using var pack = BuildPack("""{ "ambient_sounds": { "+1": { "loop": false }, " 2": { "loop": false } } }""");

        Assert.True(pack.Loops(1));
        Assert.True(pack.Loops(2));
    }

    [Fact]
    public void Loops_AcceptsTheDeferredIntervalFields()
    {
        using var pack = BuildPack("""{ "ambient_sounds": { "1": { "mode": "interval", "play": 180, "silence": 120 } } }""");

        Assert.True(pack.Loops(1));
    }

    [Fact]
    public void TryGetAmbientBytes_FindsAnEntryByItsAmbientId()
    {
        using var pack = BuildPack("{}", "amb_0001.wav", "amb_7.mp3");

        Assert.True(pack.TryGetAmbientBytes(1, out var one));
        Assert.Equal("amb_0001.wav", Encoding.ASCII.GetString(one!));
        Assert.True(pack.TryGetAmbientBytes(7, out _));
        Assert.False(pack.TryGetAmbientBytes(2, out _));
    }

    [Fact]
    public void TryGetAmbientBytes_IgnoresOtherPrefixesExtensionsAndFolders()
    {
        using var pack = BuildPack("{}", "sfx_1.wav", "amb_2.mus", "beds/amb_3.wav");

        Assert.False(pack.TryGetAmbientBytes(1, out _));
        Assert.False(pack.TryGetAmbientBytes(2, out _));
        Assert.False(pack.TryGetAmbientBytes(3, out _));
    }

    [Fact]
    public void TryGetAmbientBytes_IgnoresTheCaseOfTheEntryName()
    {
        using var pack = BuildPack("{}", "AMB_0003.WAV");

        Assert.True(pack.TryGetAmbientBytes(3, out _));
    }

    [Fact]
    public void TryGetAmbientBytes_HasNoBytesForAnIdOutsideOneTo255()
    {
        using var pack = BuildPack("{}", "amb_0.wav", "amb_255.wav", "amb_256.wav");

        Assert.False(pack.TryGetAmbientBytes(0, out _));
        Assert.True(pack.TryGetAmbientBytes(255, out _));
        Assert.False(pack.TryGetAmbientBytes(256, out _));
        Assert.False(pack.TryGetAmbientBytes(-1, out _));
    }

    [Fact]
    public void Covers_IsTrue_OnlyForAnIdThatHasAnEntry_InOneTo255()
    {
        var pack = BuildPack("{}", "amb_0001.wav", "AMB_7.OGG", "amb_0.wav", "amb_256.wav");

        Assert.True(pack.Covers(1));
        Assert.True(pack.Covers(7));
        Assert.False(pack.Covers(2));
        Assert.False(pack.Covers(0));
        Assert.False(pack.Covers(256));
    }

    [Fact]
    public void Constructor_LoadsThePack_WhenTheCoversValuesAreNull()
    {
        using var nullEntry = BuildPack("""{ "ambient_sounds": null }""", "amb_1.wav");
        using var nullCovers = BuildPack("null", "amb_1.wav");

        Assert.True(nullEntry.Loops(1));
        Assert.True(nullEntry.TryGetAmbientBytes(1, out _));
        Assert.True(nullCovers.Loops(1));
    }

    [Fact]
    public void Manifest_StillBindsTheNamedCoversMembers_AndKeepsOnlyTheOthersAside()
    {
        var manifest = JsonSerializer.Deserialize<AssetPackManifest>("""
                                                                    {
                                                                      "schema_version": 1,
                                                                      "pack_id": "test",
                                                                      "content_type": "npc_portraits",
                                                                      "covers": {
                                                                        "npc_portraits": {
                                                                          "dimensions": [200, 200],
                                                                          "dyeable": [3],
                                                                          "portraits": { "inn.spf": "inn.png" }
                                                                        }
                                                                      }
                                                                    }
                                                                    """)!;

        var entry = manifest.Covers["npc_portraits"];

        Assert.Equal([200, 200], entry.Dimensions!);
        Assert.Equal([3], entry.Dyeable!);
        Assert.Equal("inn.png", entry.Portraits!["inn.spf"]);
        Assert.Null(entry.OtherMembers);
    }
}
