using Brigid.Data.AssetPacks;
using Xunit;

namespace Brigid.Tests;

// On Linux the registry once found packs with the SearchOption overload of Directory.EnumerateFiles, which matches case
// there, so a pack named Wood.DATF was never registered. Windows and macOS matched it.
public sealed class AssetPackDiscoveryTests : IDisposable
{
    private readonly string Root = Path.Combine(Path.GetTempPath(), "brigid-packs-" + Guid.NewGuid().ToString("N"));

    public AssetPackDiscoveryTests() => Directory.CreateDirectory(Root);

    public void Dispose() => Directory.Delete(Root, true);

    private string[] Discovered()
        => AssetPackRegistry.EnumeratePackFiles(Root).Select(Path.GetFileName).Order(StringComparer.Ordinal).ToArray()!;

    private void Touch(params string[] segments)
    {
        var path = Path.Combine([Root, .. segments]);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, [1]);
    }

    [Fact]
    public void EnumeratePackFiles_FindsAPackWhateverTheCaseOfItsExtension()
    {
        Touch("stone.datf");
        Touch("Wood.DATF");
        Touch("Items.Datf");

        Assert.Equal(["Items.Datf", "Wood.DATF", "stone.datf"], Discovered());
    }

    [Fact]
    public void EnumeratePackFiles_KeepsTheOtherRulesOfTheOldSearch()
    {
        Touch(".hidden.datf");
        Touch("notes.txt");
        Touch("stone.datf.bak");
        Touch("nested", "deep.datf");

        Assert.Equal([".hidden.datf"], Discovered());
    }
}
