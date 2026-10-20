#region
using Brigid.Collections;
using Brigid.Controls.Components;
using Brigid.Controls.Generic;
using Brigid.Controls.World.Popups.Profile;
using Brigid.Data.Models;
using Brigid.Definitions;
using Brigid.Rendering;
using Brigid.Rendering.Definitions;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Xunit;
#endregion

namespace Brigid.Tests;

/// <summary>
///     The BRIG-122 status book detail popups: every child has a lowercase name, the popups are modal, a click on the popup,
///     Escape and Enter close it (Alt+Enter does not), the rows carry the requirement colours, a caption hides with no
///     value, and the height fits the description up to a limit, after which the wheel scrolls it.
/// </summary>
[Collection("FontEngine")]
public class StatusBookDetailsTests
{
    public StatusBookDetailsTests() => FontEngine.Initialize(0);

    public static TheoryData<string> Popups => ["ability", "event"];

    private static StatusBookDetailsPanel Create(string popup)
        => popup == "ability" ? new AbilityMetadataDetailsControl() : new EventMetadataDetailsControl();

    [Theory]
    [MemberData(nameof(Popups))]
    public void EveryChildHasAUniqueName(string popup)
    {
        var panel = Create(popup);

        UiAssert.NamedTree(panel);
        Assert.True(panel.IsModal);
    }

    [Theory]
    [MemberData(nameof(Popups))]
    public void EscapeEnterAndAClickClose(string popup)
    {
        var panel = Create(popup);

        foreach (var key in new[] { Keys.Escape, Keys.Enter })
        {
            panel.Show();
            var e = new KeyDownEvent { Key = key };
            panel.OnKeyDown(e);

            Assert.True(e.Handled);
            Assert.False(panel.Visible);
        }

        panel.Show();
        panel.OnClick(new ClickEvent { Button = MouseButton.Left });

        Assert.False(panel.Visible);
    }

    [Theory]
    [MemberData(nameof(Popups))]
    public void AltEnterDoesNotClose(string popup)
    {
        var panel = Create(popup);
        panel.Show();

        panel.OnKeyDown(new KeyDownEvent { Key = Keys.Enter, Modifiers = KeyModifiers.Alt });

        Assert.True(panel.Visible);
    }

    [Fact]
    public void TheStatCaptionsAndValuesShareARow()
    {
        var panel = new AbilityMetadataDetailsControl();
        var caption = panel.FindChild<UILabel>("dex_caption")!;
        var value = panel.FindChild<UILabel>("dex")!;

        Assert.Equal("DEX", caption.Text);
        Assert.Equal(caption.Y, value.Y);
        Assert.True(value.X >= caption.X + caption.Width);
        Assert.True(value.X + value.Width <= panel.ContentBounds.Right);
    }
    [Fact]
    public void TheAbilityRowsCarryTheRequirementColours()
    {
        var panel = new AbilityMetadataDetailsControl();
        var level = panel.FindChild<UILabel>("level")!;
        var str = panel.FindChild<UILabel>("str")!;

        //with no attributes from the server, no requirement is met
        panel.Populate(new AbilityMetadataEntry { Name = "Strike", IsSpell = false, Level = 10, Str = 3 });

        Assert.Equal("level 10", level.Text);
        Assert.Equal(DialogPalette.RequirementUnmet, level.ForegroundColor);
        Assert.Equal("3", str.Text);
        Assert.Equal(DialogPalette.RequirementUnmet, str.ForegroundColor);

        panel.Populate(new AbilityMetadataEntry { Name = "Strike", IsSpell = false, AbilityLevel = 5 });

        Assert.Equal("ability 5", level.Text);

        var wasMaster = WorldState.IsMaster;

        try
        {
            WorldState.IsMaster = true;
            panel.Populate(new AbilityMetadataEntry { Name = "Strike", IsSpell = false, RequiresMaster = true });

            Assert.Equal("master", level.Text);
            Assert.Equal(LegendColors.White, level.ForegroundColor);
        } finally
        {
            WorldState.IsMaster = wasMaster;
        }
    }

    [Fact]
    public void ACaptionHidesWithNoValue()
    {
        var ability = new AbilityMetadataDetailsControl();
        ability.Populate(new AbilityMetadataEntry { Name = "Strike", IsSpell = false });

        Assert.False(ability.FindChild<UILabel>("prereq_caption")!.Visible);

        ability.Populate(new AbilityMetadataEntry { Name = "Strike", IsSpell = false, PreReq2Name = "Assail", PreReq2Level = 3 });

        Assert.True(ability.FindChild<UILabel>("prereq_caption")!.Visible);
        Assert.Equal("Assail 3", ability.FindChild<UILabel>("prereq2")!.Text);

        var events = new EventMetadataDetailsControl();
        events.Populate(new EventMetadataEntry { Title = "Quest", Page = 6, Reward = "Gold" });

        Assert.Equal("master", events.FindChild<UILabel>("level")!.Text);
        Assert.False(events.FindChild<UILabel>("prereq_caption")!.Visible);
        Assert.True(events.FindChild<UILabel>("reward_caption")!.Visible);
    }

    [Fact]
    public void TheHeightFitsTheDescriptionAndTheWheelScrollsALongOne()
    {
        var panel = new TestDetailsPanel();
        var description = panel.FindChild<UILabel>("description")!;
        var viewport = new Rectangle(0, 0, 640, 480);

        panel.ShowFor("Short.", viewport);
        var shortHeight = panel.Height;

        panel.ShowFor(string.Join("\n", Enumerable.Repeat("A line.", 10)), viewport);

        Assert.Equal(shortHeight + 7 * TextRenderer.CHAR_HEIGHT, panel.Height);
        Assert.True(description.Y + description.Height <= panel.ContentBounds.Bottom);

        panel.ShowFor(string.Join("\n", Enumerable.Repeat("A line.", 20)), viewport);

        Assert.Equal(14 * TextRenderer.CHAR_HEIGHT, description.Height);

        panel.OnMouseScroll(new MouseScrollEvent { Delta = -2 });

        Assert.Equal(2 * TextRenderer.CHAR_HEIGHT, description.ScrollOffset);

        panel.OnMouseScroll(new MouseScrollEvent { Delta = -50 });

        Assert.Equal(6 * TextRenderer.CHAR_HEIGHT, description.ScrollOffset);
    }

    //a detail popup with no rows of its own, so that the base can show a description without game data
    private sealed class TestDetailsPanel() : StatusBookDetailsPanel("testdetails", 1, 0)
    {
        public void ShowFor(string description, Rectangle viewport) => ShowEntry("Title", description, viewport);
    }
}
