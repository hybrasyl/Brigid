#region
using System.IO.Compression;
using System.Text;
using Brigid.Data.AssetPacks;
using Brigid.Rendering;
using SkiaSharp;
using Xunit;
#endregion

namespace Brigid.Tests;

[CollectionDefinition("AssetPackRegistry", DisableParallelization = true)]
public sealed class AssetPackRegistryCollection;

/// <summary>
///     Several packs of one content type (BRIG-15). A lookup asks the packs in order of priority, the highest first, and
///     at the same priority in registration order: a pack with no entry, or with an entry that does not decode, passes
///     the lookup to the next pack, so the first pack that covers a key wins. Each test registers in-memory archives, and resets the static registry before and after.
/// </summary>
[Collection("AssetPackRegistry")]
public sealed class AssetPackRegistryTests : IDisposable
{
    private static readonly SKColor First = SKColors.Red;
    private static readonly SKColor Second = SKColors.Blue;

    public AssetPackRegistryTests() => AssetPackRegistry.ResetForTest();

    public void Dispose() => AssetPackRegistry.ResetForTest();

    private static byte[] Png(SKColor color, int size = 2)
    {
        using var bitmap = new SKBitmap(size, size);
        bitmap.Erase(color);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);

        return data.ToArray();
    }

    private static void Register(string contentType, string packId, Dictionary<string, byte[]> entries, AssetPackManifest? manifest = null)
    {
        var stream = new MemoryStream();

        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true))
            foreach ((var name, var bytes) in entries)
            {
                using var entry = zip.CreateEntry(name).Open();
                entry.Write(bytes);
            }

        stream.Position = 0;

        manifest ??= new AssetPackManifest { SchemaVersion = 1, PackId = packId, ContentType = contentType };

        Assert.True(AssetPackRegistry.RegisterForTest(new ZipArchive(stream, ZipArchiveMode.Read), manifest));
    }

    private static AssetPackManifest Manifest(string contentType, string packId, int priority)
        => new() { SchemaVersion = 1, PackId = packId, ContentType = contentType, Priority = priority };

    private static SKColor ColorOf(SKImage? image)
    {
        Assert.NotNull(image);

        using (image)
        {
            using var bitmap = SKBitmap.FromImage(image);

            return bitmap.GetPixel(0, 0);
        }
    }

    public static TheoryData<string, string, string> ImageLookups()
        => new()
        {
            { "ability_icons", "skill0001.png", "icon" },
            { "nation_badges", "nation0001.png", "badge" },
            { "static_tiles", "floor00001.png", "floor" },
            { "static_tiles", "wall00001.png", "wall" },
            { "legend_mark_icons", "legend0001.png", "legend" },
            { "ui_sprite_overrides", "x.epf/0000.png", "ui" },
            { "world_maps", "field001.png", "field" },
            { "town_maps", "town_00001.png", "town" }
        };

    private static bool Lookup(string kind, out SKImage? image)
        => kind switch
        {
            "icon"   => AssetPackRegistry.TryGetIconImage("skill", 1, out image),
            "badge"  => AssetPackRegistry.TryGetBadgeImage(1, out image),
            "floor"  => AssetPackRegistry.TryGetFloorImage(1, out image),
            "wall"   => AssetPackRegistry.TryGetWallImage(1, out image),
            "legend" => AssetPackRegistry.TryGetLegendMarkImage(1, out image),
            "ui"     => AssetPackRegistry.TryGetUiSprite("x.epf", 0, out image),
            "field"  => AssetPackRegistry.TryGetFieldImage("field001", out image),
            "town"   => AssetPackRegistry.TryGetTownMapImage(1, out image),
            _        => throw new ArgumentOutOfRangeException(nameof(kind))
        };

    [Theory]
    [MemberData(nameof(ImageLookups))]
    public void Lookup_UsesThePackThatRegisteredFirst(string contentType, string entry, string kind)
    {
        Register(contentType, "first", new() { [entry] = Png(First) });
        Register(contentType, "second", new() { [entry] = Png(Second) });

        Assert.True(Lookup(kind, out var image));
        Assert.Equal(First, ColorOf(image));
    }

    [Theory]
    [MemberData(nameof(ImageLookups))]
    public void Lookup_AsksTheNextPack_WhenTheFirstHasNoEntry(string contentType, string entry, string kind)
    {
        Register(contentType, "first", new() { ["unrelated.png"] = Png(First) });
        Register(contentType, "second", new() { [entry] = Png(Second) });

        Assert.True(Lookup(kind, out var image));
        Assert.Equal(Second, ColorOf(image));
    }

    [Theory]
    [MemberData(nameof(ImageLookups))]
    public void Lookup_AsksTheNextPack_WhenTheFirstEntryDoesNotDecode(string contentType, string entry, string kind)
    {
        Register(contentType, "first", new() { [entry] = "not a png"u8.ToArray() });
        Register(contentType, "second", new() { [entry] = Png(Second) });

        Assert.True(Lookup(kind, out var image));
        Assert.Equal(Second, ColorOf(image));
    }

    [Theory]
    [MemberData(nameof(ImageLookups))]
    public void Lookup_IsFalse_WhenNoPackCoversTheKey(string contentType, string _, string kind)
    {
        Register(contentType, "first", new() { ["unrelated.png"] = Png(First) });

        Assert.False(Lookup(kind, out var image));
        Assert.Null(image);
    }

    [Fact]
    public void NpcIllustration_UsesTheFirstPackWithAValidPortrait()
    {
        static AssetPackManifest PortraitManifest(string id, int size)
            => new()
            {
                SchemaVersion = 1,
                PackId = id,
                ContentType = "npc_portraits",
                Covers = new Dictionary<string, AssetPackCoverageEntry>
                {
                    ["npc_portraits"] = new() { Dimensions = [size, size], Portraits = new() { ["inn.spf"] = "inn.png" } }
                }
            };

        //the first pack promises 4x4 but ships 2x2, so it refuses the portrait and the second pack answers
        Register("npc_portraits", "first", new() { ["inn.png"] = Png(First) }, PortraitManifest("first", 4));
        Register("npc_portraits", "second", new() { ["inn.png"] = Png(Second) }, PortraitManifest("second", 2));

        Assert.True(AssetPackRegistry.TryGetNpcIllustration("inn.spf", out var image));
        Assert.Equal(Second, ColorOf(image));
    }

    [Fact]
    public void GetCreaturePackFor_GivesTheFirstPackThatCoversTheSprite()
    {
        Register("creature_sprites", "a", new() { ["creature_sprites/creature_00007/stand/n_001.png"] = Png(First) });
        Register("creature_sprites", "b", new()
        {
            ["creature_sprites/creature_00007/stand/n_001.png"] = Png(Second),
            ["creature_sprites/creature_00008/stand/e_001.png"] = Png(Second)
        });

        Assert.Equal("a", AssetPackRegistry.GetCreaturePackFor(7)?.Manifest.PackId);
        Assert.Equal("b", AssetPackRegistry.GetCreaturePackFor(8)?.Manifest.PackId);
        Assert.Null(AssetPackRegistry.GetCreaturePackFor(9));
    }

    [Fact]
    public void AudioPacks_AreInRegistrationOrder()
    {
        Register("sound_effects", "a", new() { ["sfx_1.wav"] = [1] });
        Register("sound_effects", "b", new() { ["sfx_1.wav"] = [2], ["sfx_2.wav"] = [3] });

        Assert.Equal(["a", "b"], AssetPackRegistry.SfxPacks.Select(pack => pack.Manifest.PackId));
        Assert.Empty(AssetPackRegistry.MusicPacks);
    }

    [Fact]
    public void HasStaticTilePacks_FollowsRegistration()
    {
        Assert.False(AssetPackRegistry.HasStaticTilePacks);

        Register("static_tiles", "a", new() { ["floor00001.png"] = Png(First) });

        Assert.True(AssetPackRegistry.HasStaticTilePacks);
    }

    [Fact]
    public void RegisterForTest_RefusesAnUnknownContentType()
    {
        using var archive = new ZipArchive(new MemoryStream(EmptyZip()), ZipArchiveMode.Read);

        Assert.False(AssetPackRegistry.RegisterForTest(archive, new AssetPackManifest { SchemaVersion = 1, PackId = "x", ContentType = "no_such_type" }));
    }

    [Fact]
    public void Lookup_UsesTheHigherPriority_WhenItRegisteredLater()
    {
        Register("ability_icons", "first", new() { ["skill0001.png"] = Png(First) });
        Register("ability_icons", "second", new() { ["skill0001.png"] = Png(Second) }, Manifest("ability_icons", "second", 500));

        Assert.True(AssetPackRegistry.TryGetIconImage("skill", 1, out var image));
        Assert.Equal(Second, ColorOf(image));
    }

    [Fact]
    public void Lookup_PutsALowerPriorityAfterTheDefault_WhenItRegisteredFirst()
    {
        Register("ability_icons", "first", new() { ["skill0001.png"] = Png(First) }, Manifest("ability_icons", "first", 50));
        Register("ability_icons", "second", new() { ["skill0001.png"] = Png(Second) });

        Assert.True(AssetPackRegistry.TryGetIconImage("skill", 1, out var image));
        Assert.Equal(Second, ColorOf(image));
    }

    [Fact]
    public void Lookup_KeepsRegistrationOrder_AmongEqualPriorities()
    {
        Register("ability_icons", "high", new() { ["skill0002.png"] = Png(Second) }, Manifest("ability_icons", "high", 200));
        Register("ability_icons", "first", new() { ["skill0001.png"] = Png(First) }, Manifest("ability_icons", "first", 150));
        Register("ability_icons", "second", new() { ["skill0001.png"] = Png(Second) }, Manifest("ability_icons", "second", 150));

        Assert.True(AssetPackRegistry.TryGetIconImage("skill", 1, out var image));
        Assert.Equal(First, ColorOf(image));
    }

    [Fact]
    public void Registration_NamesThePackAskedFirst_ByPriority()
    {
        var warnings = new List<string>();
        void Collect(string line) => warnings.Add(line);

        AssetPackRegistry.WarningLogged += Collect;

        try
        {
            Register("ability_icons", "base", new() { ["skill0001.png"] = Png(First) });
            Register("ability_icons", "holiday", new() { ["skill0001.png"] = Png(Second) }, Manifest("ability_icons", "holiday", 200));
        }
        finally
        {
            AssetPackRegistry.WarningLogged -= Collect;
        }

        var warning = Assert.Single(warnings);
        Assert.Contains("'holiday' is asked first, before 'base': its priority 200 is higher than 100", warning);
        Assert.Contains("An entry of 'holiday' that does not decode passes the lookup to 'base'", warning);
    }

    [Fact]
    public void Registration_NamesTheEarlierPack_WhenTheNewPackHasALowerPriority()
    {
        var warnings = new List<string>();
        void Collect(string line) => warnings.Add(line);

        AssetPackRegistry.WarningLogged += Collect;

        try
        {
            Register("ability_icons", "holiday", new() { ["skill0001.png"] = Png(Second) }, Manifest("ability_icons", "holiday", 200));
            Register("ability_icons", "base", new() { ["skill0001.png"] = Png(First) });
        }
        finally
        {
            AssetPackRegistry.WarningLogged -= Collect;
        }

        Assert.Contains("'holiday' is asked first, before 'base': its priority 200 is higher than 100", Assert.Single(warnings));
    }

    [Fact]
    public void Lookup_KeepsPriorityOrder_WhenALowerPackRegistersBetweenHigherOnes()
    {
        Register("ability_icons", "low", new() { ["skill0001.png"] = Png(First) }, Manifest("ability_icons", "low", -5));
        Register("ability_icons", "high", new() { ["skill0002.png"] = Png(Second) }, Manifest("ability_icons", "high", 300));
        Register("ability_icons", "middle", new() { ["skill0001.png"] = Png(Second) });

        Assert.True(AssetPackRegistry.TryGetIconImage("skill", 1, out var image));
        Assert.Equal(Second, ColorOf(image));
    }

    [Fact]
    public void GetCreaturePackFor_GivesTheHigherPriorityPack()
    {
        Register("creature_sprites", "base", new() { ["creature_sprites/creature_00007/stand/n_001.png"] = Png(First) });
        Register("creature_sprites", "holiday", new() { ["creature_sprites/creature_00007/stand/n_001.png"] = Png(Second) },
            Manifest("creature_sprites", "holiday", 200));

        Assert.Equal("holiday", AssetPackRegistry.GetCreaturePackFor(7)?.Manifest.PackId);
    }

    [Fact]
    public void GetAmbientPackFor_GivesTheHigherPriorityPack_ThatHasTheId()
    {
        Register("ambient_sounds", "base", new() { ["amb_0001.wav"] = [1], ["amb_0002.wav"] = [1] });
        Register("ambient_sounds", "holiday", new() { ["amb_0001.ogg"] = [2] }, Manifest("ambient_sounds", "holiday", 200));

        Assert.Equal(["holiday", "base"], AssetPackRegistry.AmbientPacks.Select(pack => pack.Manifest.PackId));
        Assert.Equal("holiday", AssetPackRegistry.GetAmbientPackFor(1)?.Manifest.PackId);
        Assert.Equal("base", AssetPackRegistry.GetAmbientPackFor(2)?.Manifest.PackId);
        Assert.Null(AssetPackRegistry.GetAmbientPackFor(3));
    }

    [Fact]
    public void GetAmbientPackFor_GivesNoPack_ForAnIdOutsideOneTo255()
    {
        Register("ambient_sounds", "base", new() { ["amb_0000.wav"] = [1], ["amb_256.wav"] = [1] });

        Assert.Null(AssetPackRegistry.GetAmbientPackFor(0));
        Assert.Null(AssetPackRegistry.GetAmbientPackFor(256));
    }

    [Fact]
    public void Registration_WarnsAboutSharedKeys_AndNamesThePackAskedFirst()
    {
        var warnings = new List<string>();
        void Collect(string line) => warnings.Add(line);

        AssetPackRegistry.WarningLogged += Collect;

        try
        {
            Register("ability_icons", "first", new() { ["skill0001.png"] = Png(First), ["skill0002.png"] = Png(First) });
            Register("ability_icons", "second", new() { ["skill0002.png"] = Png(Second), ["skill0003.png"] = Png(Second) });
            Register("ability_icons", "third", new() { ["skill0009.png"] = Png(Second) });
        }
        finally
        {
            AssetPackRegistry.WarningLogged -= Collect;
        }

        var warning = Assert.Single(warnings);
        Assert.Contains("'first' and 'second' share 1 key(s)", warning);
        Assert.Contains("skill0002.png", warning);
        Assert.Contains("'first' is asked first, before 'second': it registered first, at the same priority", warning);
    }

    private static void RegisterTheme(string packId, int priority, bool withFrame)
    {
        var json = withFrame ? """{ "parts": { "frame.panel": { "file": "frame.png" } } }""" : "{}";
        var entries = new Dictionary<string, byte[]> { [UiThemePack.THEME_ENTRY_NAME] = Encoding.UTF8.GetBytes(json) };

        if (withFrame)
            entries["frame.png"] = Png(First);

        Register("ui_theme", packId, entries, Manifest("ui_theme", packId, priority));
    }

    [Fact]
    public void ASecondPackWithAPackIdThatIsRegistered_IsRefused_AcrossTypesAndCase()
    {
        var warnings = new List<string>();
        void Collect(string line) => warnings.Add(line);

        Register("ability_icons", "shared", new() { ["skill0001.png"] = Png(First) });
        AssetPackRegistry.WarningLogged += Collect;

        try
        {
            var stream = new MemoryStream();

            using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true))
            using (var entry = zip.CreateEntry("nation0001.png").Open())
                entry.Write(Png(Second));

            stream.Position = 0;

            Assert.False(AssetPackRegistry.RegisterForTest(
                new ZipArchive(stream, ZipArchiveMode.Read),
                new AssetPackManifest { SchemaVersion = 1, PackId = "SHARED", ContentType = "nation_badges" }));
        }
        finally
        {
            AssetPackRegistry.WarningLogged -= Collect;
        }

        Assert.Empty(AssetPackRegistry.NationBadgePacks);
        Assert.Equal(["shared"], AssetPackRegistry.IconPacks.Select(pack => pack.Manifest.PackId));
        Assert.Contains("has pack_id 'SHARED', which the registered ability_icons pack 'shared' already has", Assert.Single(warnings));
    }

    [Fact]
    public void UiTheme_IsTheFirstPack_WhenNoThemeIsSelected()
    {
        RegisterTheme("wood", 100, false);
        RegisterTheme("stone", 200, false);

        Assert.Equal(["stone", "wood"], AssetPackRegistry.UiThemePacks.Select(pack => pack.Manifest.PackId));
        Assert.Equal("stone", AssetPackRegistry.GetUiThemePack()?.Manifest.PackId);
    }

    [Fact]
    public void UiTheme_IsTheSelectedPack_IgnoringCase()
    {
        RegisterTheme("wood", 100, false);
        RegisterTheme("stone", 200, false);

        AssetPackRegistry.SelectUiTheme("WOOD");
        Assert.Equal("wood", AssetPackRegistry.GetUiThemePack()?.Manifest.PackId);

        AssetPackRegistry.SelectUiTheme(null);
        Assert.Equal("stone", AssetPackRegistry.GetUiThemePack()?.Manifest.PackId);
    }

    [Fact]
    public void UiTheme_ASelectionMadeBeforeThePackRegisters_TakesEffectWhenItRegisters()
    {
        AssetPackRegistry.SelectUiTheme("wood");
        RegisterTheme("stone", 200, false);
        Assert.Equal("stone", AssetPackRegistry.GetUiThemePack()?.Manifest.PackId);

        RegisterTheme("wood", 100, false);
        Assert.Equal("wood", AssetPackRegistry.GetUiThemePack()?.Manifest.PackId);
    }

    [Fact]
    public void UiTheme_ASelectionThatIsNotInstalled_UsesTheFirstPack_AndWarns()
    {
        var warnings = new List<string>();
        void Collect(string line) => warnings.Add(line);

        RegisterTheme("wood", 100, false);
        AssetPackRegistry.WarningLogged += Collect;

        try
        {
            AssetPackRegistry.SelectUiTheme("goth");
        }
        finally
        {
            AssetPackRegistry.WarningLogged -= Collect;
        }

        Assert.Equal("wood", AssetPackRegistry.GetUiThemePack()?.Manifest.PackId);
        Assert.Contains("the selected ui_theme 'goth' is not installed; using 'wood'", Assert.Single(warnings));
    }

    [Fact]
    public void UiTheme_ThemeArtDoesNotTakeAPartFromAnotherTheme()
    {
        RegisterTheme("wood", 100, true);
        RegisterTheme("stone", 200, false);

        Assert.False(ThemeArt.HasPart("frame.panel"));

        AssetPackRegistry.SelectUiTheme("wood");
        Assert.True(ThemeArt.HasPart("frame.panel"));
    }

    [Fact]
    public void UiThemes_ShareNoKeys_SoTwoThemesGiveNoWarning()
    {
        var warnings = new List<string>();
        void Collect(string line) => warnings.Add(line);

        AssetPackRegistry.WarningLogged += Collect;

        try
        {
            RegisterTheme("wood", 100, true);
            RegisterTheme("stone", 100, true);
        }
        finally
        {
            AssetPackRegistry.WarningLogged -= Collect;
        }

        Assert.Empty(warnings);
    }

    private static byte[] EmptyZip()
    {
        var stream = new MemoryStream();

        using (new ZipArchive(stream, ZipArchiveMode.Create, true)) { }

        return stream.ToArray();
    }

    [Fact]
    public void SharedKeys_ComparesImageEntries_IgnoringCaseAndOtherFiles()
    {
        Register("ability_icons", "a", new()
        {
            ["skill0001.png"] = Png(First), ["skill0002.png"] = Png(First),
            ["_manifest.json"] = "{}"u8.ToArray(), ["README.md"] = "a"u8.ToArray(), ["__MACOSX/skill0001.png"] = [0]
        });
        Register("ability_icons", "b", new()
        {
            ["SKILL0002.PNG"] = Png(Second), ["skill0003.png"] = Png(Second),
            ["_manifest.json"] = "{}"u8.ToArray(), ["README.md"] = "b"u8.ToArray(), ["__MACOSX/skill0001.png"] = [0]
        });

        var packs = AssetPackRegistry.IconPacks.ToList();

        Assert.Equal(["skill0002.png"], AssetPackRegistry.SharedKeys(packs[0], packs[1]), StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void SharedKeys_ComparesAudioIds_NotFileNames()
    {
        Register("sound_effects", "a", new() { ["sfx_1.wav"] = [1], ["sfx_5.wav"] = [1] });
        Register("sound_effects", "b", new() { ["sfx_0001.mp3"] = [2], ["sfx_6.wav"] = [2] });

        var packs = AssetPackRegistry.SfxPacks.ToList();

        Assert.Equal(["1"], AssetPackRegistry.SharedKeys(packs[0], packs[1]));
    }

    [Fact]
    public void SharedKeys_ComparesPortraitKeys_NotFileNames()
    {
        static AssetPackManifest Manifest(string id, string file)
            => new()
            {
                SchemaVersion = 1,
                PackId = id,
                ContentType = "npc_portraits",
                Covers = new Dictionary<string, AssetPackCoverageEntry>
                {
                    ["npc_portraits"] = new() { Dimensions = [2, 2], Portraits = new() { ["inn.spf"] = file } }
                }
            };

        Register("npc_portraits", "a", new() { ["a.png"] = Png(First) }, Manifest("a", "a.png"));
        Register("npc_portraits", "b", new() { ["b.png"] = Png(Second) }, Manifest("b", "b.png"));

        var packs = AssetPackRegistry.NpcPortraitPacks.ToList();

        Assert.Equal(["inn.spf"], AssetPackRegistry.SharedKeys(packs[0], packs[1]));
    }

    [Fact]
    public void InRegistrationOrder_SortsByFileName_IgnoringCaseAndFolder()
    {
        var ordered = AssetPackRegistry.InRegistrationOrder([
            Path.Combine("z", "items_b.datf"),
            Path.Combine("a", "Items_C.datf"),
            Path.Combine("m", "ITEMS_A.datf")
        ]).Select(Path.GetFileName);

        Assert.Equal(["ITEMS_A.datf", "items_b.datf", "Items_C.datf"], ordered);
    }

    //BRIG-129: an assets folder that exists but cannot be listed stopped the client from starting. The listing gives one
    //valid pack before it fails, so the test also shows that nothing registers from a listing that did not finish
    [Theory]
    [InlineData(typeof(UnauthorizedAccessException))]
    [InlineData(typeof(IOException))]
    public void AFolderThatCannotBeListed_LogsOneWarningAndRegistersNothing(Type error)
    {
        var dir = Directory.CreateTempSubdirectory("brigid-packs-").FullName;
        var pack = Path.Combine(dir, "icons.datf");
        var warnings = new List<string>();

        using (var zip = ZipFile.Open(pack, ZipArchiveMode.Create))
        using (var manifest = zip.CreateEntry("_manifest.json").Open())
            manifest.Write(Encoding.UTF8.GetBytes("""{"schema_version": 1, "pack_id": "icons", "content_type": "ability_icons"}"""));

        IEnumerable<string> ListThenFail(string _)
        {
            yield return pack;

            throw (Exception)Activator.CreateInstance(error, "listing refused")!;
        }

        void Collect(string line) => warnings.Add(line);

        AssetPackRegistry.WarningLogged += Collect;

        try
        {
            AssetPackRegistry.InitializeFrom(dir, ListThenFail);
        }
        finally
        {
            AssetPackRegistry.WarningLogged -= Collect;
            Directory.Delete(dir, true);
        }

        var warning = Assert.Single(warnings);
        Assert.Contains($"cannot list the asset-pack folder '{dir}', so the client starts with no packs: listing refused", warning);
        Assert.Equal(0, AssetPackRegistry.RegisteredCount);
        Assert.Equal(dir, AssetPackRegistry.UnlistableFolder);
    }
}
