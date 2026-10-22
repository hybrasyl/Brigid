#region
using System.Text;
using Brigid.Rendering;
using Xunit;
#endregion

namespace Brigid.Tests;

/// <summary>
///     The display face behind <see cref="FontStyle.Display" />: the shipped Uncial Antiqua, which a <c>ui_theme</c> pack
///     can replace. A pack entry that is not a font must be refused and must not replace the face.
/// </summary>
[Collection("FontEngine")]
public class DisplayFontTests
{
    public DisplayFontTests() => FontEngine.Initialize(0);

    [Theory]
    [InlineData(new byte[] { 0x00, 0x01, 0x00, 0x00, 0x00 }, true)]
    [InlineData(new byte[] { (byte)'O', (byte)'T', (byte)'T', (byte)'O' }, true)]
    [InlineData(new byte[] { (byte)'t', (byte)'r', (byte)'u', (byte)'e' }, true)]
    [InlineData(new byte[] { (byte)'t', (byte)'t', (byte)'c', (byte)'f' }, true)]
    [InlineData(new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G' }, false)]
    [InlineData(new byte[] { 0x00, 0x01, 0x00 }, false)]
    public void HasFontSignature_ChecksTheFirstFourBytes(byte[] bytes, bool expected)
        => Assert.Equal(expected, FontEngine.HasFontSignature(bytes));

    [Fact]
    public void Display_UsesItsOwnFace()
    {
        var engine = FontEngine.Instance;

        //the same text in the display face measures differently from the active UI face
        Assert.NotEqual(engine.MeasureWidth("Hybrasyl", 40, FontStyle.Regular), engine.MeasureWidth("Hybrasyl", 40, FontStyle.Display));
    }

    [Fact]
    public void SetDisplayFont_RefusesBytesThatAreNotAFont()
    {
        var engine = FontEngine.Instance;
        var before = engine.MeasureWidth("Hybrasyl", 40, FontStyle.Display);
        var generation = engine.Generation;

        Assert.False(engine.SetDisplayFont(Encoding.UTF8.GetBytes("not a font, only some text")));
        //a font signature on bytes that are not a font gets past the cheap check and fails in the parser
        Assert.False(engine.SetDisplayFont([0x00, 0x01, 0x00, 0x00, 0x01, 0x02, 0x03, 0x04]));

        Assert.Equal(before, engine.MeasureWidth("Hybrasyl", 40, FontStyle.Display));
        Assert.Equal(generation, engine.Generation);
    }

    [Fact]
    public void SetDisplayFont_ReplacesAndRestoresTheFace()
    {
        var engine = FontEngine.Instance;
        var shipped = engine.MeasureWidth("Hybrasyl", 40, FontStyle.Display);
        var replacement = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Content", "Fonts", "ComicShannsMono-Regular.ttf"));

        var generation = engine.Generation;

        Assert.True(engine.SetDisplayFont(replacement));
        Assert.NotEqual(shipped, engine.MeasureWidth("Hybrasyl", 40, FontStyle.Display));
        //measurement caches key on Generation, so a new face must bump it
        Assert.Equal(generation + 1, engine.Generation);

        Assert.True(engine.SetDisplayFont(null));
        Assert.Equal(shipped, engine.MeasureWidth("Hybrasyl", 40, FontStyle.Display));
        Assert.Equal(generation + 2, engine.Generation);

        //restoring the face that is already in use changes nothing
        Assert.True(engine.SetDisplayFont(null));
        Assert.Equal(generation + 2, engine.Generation);
    }
}
