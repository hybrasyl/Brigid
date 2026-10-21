using Brigid.Controls.Generic;
using Brigid.Rendering;
using Xunit;

namespace Brigid.Tests;

// BRIG-126 step 3a: a horizontal scroll bar draws its own theme parts, never the upright vertical art, and a missing
// state falls back to the normal part of the same orientation.
public class ScrollBarThemeRoleTests
{
    [Theory]
    [InlineData(false, ThemeRoles.ScrollTrack)]
    [InlineData(true, ThemeRoles.ScrollHTrack)]
    public void TheTrackTakesThePartOfItsOrientation(bool horizontal, string role)
        => Assert.Equal(role, ScrollBarControl.TrackRole(horizontal));

    [Theory]
    [InlineData(false, false, false, ThemeRoles.ScrollThumb, ThemeRoles.ScrollThumb)]
    [InlineData(false, false, true, ThemeRoles.ScrollThumbHover, ThemeRoles.ScrollThumb)]
    [InlineData(false, true, true, ThemeRoles.ScrollThumbPressed, ThemeRoles.ScrollThumb)]
    [InlineData(true, false, false, ThemeRoles.ScrollHThumb, ThemeRoles.ScrollHThumb)]
    [InlineData(true, false, true, ThemeRoles.ScrollHThumbHover, ThemeRoles.ScrollHThumb)]
    [InlineData(true, true, true, ThemeRoles.ScrollHThumbPressed, ThemeRoles.ScrollHThumb)]
    public void TheThumbTakesThePartOfItsOrientationAndState(bool horizontal, bool pressed, bool hovered, string role, string normal)
        => Assert.Equal((role, normal), ScrollBarControl.ThumbRoles(horizontal, pressed, hovered));

    [Theory]
    [InlineData(true, true, false, ThemeRoles.ScrollLeft, ThemeRoles.ScrollLeft)]
    [InlineData(true, true, true, ThemeRoles.ScrollLeftPressed, ThemeRoles.ScrollLeft)]
    [InlineData(true, false, false, ThemeRoles.ScrollRight, ThemeRoles.ScrollRight)]
    [InlineData(true, false, true, ThemeRoles.ScrollRightPressed, ThemeRoles.ScrollRight)]
    [InlineData(false, true, false, ThemeRoles.ScrollUp, ThemeRoles.ScrollUp)]
    [InlineData(false, true, true, ThemeRoles.ScrollUpPressed, ThemeRoles.ScrollUp)]
    [InlineData(false, false, false, ThemeRoles.ScrollDown, ThemeRoles.ScrollDown)]
    [InlineData(false, false, true, ThemeRoles.ScrollDownPressed, ThemeRoles.ScrollDown)]
    public void AnArrowTakesThePartOfItsOrientationAndEnd(bool horizontal, bool decrement, bool pressed, string role, string normal)
        => Assert.Equal((role, normal), ScrollBarControl.ArrowRoles(horizontal, decrement, pressed));
}
