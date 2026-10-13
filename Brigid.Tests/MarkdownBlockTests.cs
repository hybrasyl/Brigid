#region
using Brigid.Controls.Generic;
using Brigid.Rendering;
using Microsoft.Xna.Framework;
using Xunit;
#endregion

namespace Brigid.Tests;

/// <summary>
///     <see cref="MarkdownBlock" /> lays its markdown out again when its width changes, and tells its host.
/// </summary>
[Collection("FontEngine")]
public class MarkdownBlockTests
{
    public MarkdownBlockTests() => FontEngine.Initialize(0);

    [Fact]
    public void AMarkdownBlockLaysOutAgainForANewWidth()
    {
        var block = new MarkdownBlock { Width = 400, Height = 200 };
        var layouts = 0;
        block.LaidOut += () => layouts++;

        block.SetMarkdown(string.Concat(Enumerable.Repeat("word ", 60)));
        var wide = block.ContentHeight;

        block.EnsureLayout();
        Assert.Equal(1, layouts);

        block.Width = 100;
        block.Update(new GameTime());

        Assert.Equal(2, layouts);
        Assert.True(block.ContentHeight > wide);
    }

    [Fact]
    public void AMarkdownBlockLaysOutAgainAfterAResize()
    {
        var engine = FontEngine.Instance;
        var block = new MarkdownBlock { Width = 400, Height = 200 };
        var layouts = 0;
        block.LaidOut += () => layouts++;
        block.SetMarkdown("word word word");

        try
        {
            //a window resize changes the layout scale and bumps FontEngine.Generation
            engine.SetLayoutScale(1.75f, 1.75f);
            block.Update(new GameTime());

            Assert.Equal(2, layouts);
        } finally
        {
            engine.SetLayoutScale(1f, 1f);
        }
    }
}
