using Brigid.Controls.World.Hud.Panel.Slots;
using Xunit;

namespace Brigid.Tests;

// A spell slot copied its chants only when the new value was not null, so a spell with no saved chants kept the chants
// of the slot before it, cast with them, and the next save wrote them back. The slot's ten lines are replaced now.
public sealed class SpellChantSlotTests
{
    private static string[] OldChants() => Enumerable.Range(0, 10).Select(i => $"old {i}").ToArray();

    [Fact]
    public void NoSavedChantsClearsTheOldLines()
    {
        var lines = OldChants();

        SpellSlot.CopyChants(null, lines);

        Assert.All(lines, line => Assert.Equal(string.Empty, line));
    }

    [Fact]
    public void FewerSavedLinesClearTheRest()
    {
        var lines = OldChants();

        SpellSlot.CopyChants(["new 0", "new 1"], lines);

        Assert.Equal(["new 0", "new 1", "", "", "", "", "", "", "", ""], lines);
    }

    [Fact]
    public void MoreThanTenLinesAreCut()
    {
        var lines = OldChants();

        SpellSlot.CopyChants(Enumerable.Range(0, 12).Select(i => $"new {i}").ToArray(), lines);

        Assert.Equal(Enumerable.Range(0, 10).Select(i => $"new {i}"), lines);
    }
}
