#region
using Brigid.Controls.Components;
using Brigid.Controls.Generic;
using Brigid.Data.AssetPacks;
using Brigid.Rendering;
using Chaos.DarkAges.Definitions;
using Microsoft.Xna.Framework.Graphics;
using System.Reflection;
using Xunit;
#endregion

namespace Brigid.Tests;

/// <summary>
///     The BRIG-122 social status icons: each status has its <c>icon.status.*</c> role, a theme part or Brigid's own image
///     replaces the legacy frame, and the legacy frame is read only when there is no new art. On the HUD emote button, new
///     art shows in the icon over the button, and the legacy frame stays the button's own texture.
/// </summary>
[Collection("FontEngine")]
public sealed class StatusIconTests : IDisposable
{
    private UiThemePack? Theme;

    public void Dispose()
    {
        AssetPackRegistry.SetUiThemeForTests(null);
        BuiltInArt.SetEnabledForTests(true);
        Theme?.Dispose();
    }

    [Theory]
    [InlineData(SocialStatus.Awake, "icon.status.awake")]
    [InlineData(SocialStatus.DoNotDisturb, "icon.status.do_not_disturb")]
    [InlineData(SocialStatus.DayDreaming, "icon.status.daydreaming")]
    [InlineData(SocialStatus.NeedGroup, "icon.status.need_group")]
    [InlineData(SocialStatus.Grouped, "icon.status.grouped")]
    [InlineData(SocialStatus.LoneHunter, "icon.status.lone_hunter")]
    [InlineData(SocialStatus.GroupHunting, "icon.status.group_hunting")]
    [InlineData(SocialStatus.NeedHelp, "icon.status.need_help")]
    public void EachStatusHasItsRole(SocialStatus status, string role) => Assert.Equal(role, StatusIcon.RoleFor(status));

    [Fact]
    public void AnUnknownStatusHasNoRoleAndNoArt()
    {
        InstallTheme();

        Assert.Null(StatusIcon.RoleFor((SocialStatus)8));
        Assert.False(StatusIcon.HasArt((SocialStatus)8));
    }

    [Fact]
    public void AThemePartIsArtForItsStatusOnly()
    {
        InstallTheme();
        BuiltInArt.SetEnabledForTests(false);

        Assert.True(StatusIcon.HasArt(SocialStatus.Awake));
        Assert.False(StatusIcon.HasArt(SocialStatus.NeedHelp));
    }

    [Fact]
    public void BrigidHasNoImageForARoleThatItDoesNotShip() => Assert.False(BuiltInArt.Has("icon.status.not_a_status"));

    [Theory]
    [InlineData(SocialStatus.Awake)]
    [InlineData(SocialStatus.DoNotDisturb)]
    [InlineData(SocialStatus.DayDreaming)]
    [InlineData(SocialStatus.NeedGroup)]
    [InlineData(SocialStatus.Grouped)]
    [InlineData(SocialStatus.LoneHunter)]
    [InlineData(SocialStatus.GroupHunting)]
    [InlineData(SocialStatus.NeedHelp)]
    public void WithNoThemeEveryStatusHasBrigidsOwnImage(SocialStatus status)
    {
        Assert.True(BuiltInArt.Has(StatusIcon.RoleFor(status)));
        Assert.True(StatusIcon.HasArt(status));
    }

    [Fact]
    public void AListedPartWithNoImageIsNotArt()
    {
        Theme = UiThemeTests.BuildPack("""{ "parts": { "icon.status.awake": { "file": "missing.png" } } }""");
        AssetPackRegistry.SetUiThemeForTests(Theme);
        BuiltInArt.SetEnabledForTests(false);

        Assert.False(StatusIcon.HasArt(SocialStatus.Awake));
    }

    [Fact]
    public void EveryBuiltInImageIsNamedForARole()
    {
        const string PREFIX = "Brigid.Rendering.BuiltIn.";
        var roles = typeof(ThemeRoles)
                    .GetFields(BindingFlags.Public | BindingFlags.Static)
                    .Where(field => field.IsLiteral)
                    .Select(field => (string)field.GetRawConstantValue()!)
                    .ToHashSet();

        var images = typeof(BuiltInArt).Assembly.GetManifestResourceNames().Where(name => name.StartsWith(PREFIX, StringComparison.Ordinal));

        Assert.All(images, name => Assert.Contains(name[PREFIX.Length..^".png".Length], roles));
    }

    [Fact]
    public void WithArtTheLegacyFrameIsNotRead()
    {
        InstallTheme();
        var icon = new StatusIcon();
        var reads = 0;

        var hasArt = icon.Show(SocialStatus.Awake, _ => { reads++; return null; });

        Assert.True(hasArt);
        Assert.Equal(SocialStatus.Awake, icon.Status);
        Assert.Null(icon.LegacyTexture);
        Assert.Equal(0, reads);
    }

    [Fact]
    public void WithNoArtTheLegacyFrameIsRead()
    {
        InstallTheme();
        BuiltInArt.SetEnabledForTests(false);
        var icon = new StatusIcon();
        SocialStatus? read = null;

        var hasArt = icon.Show(SocialStatus.NeedHelp, status => { read = status; return null; });

        Assert.Equal(SocialStatus.NeedHelp, read);
        Assert.False(hasArt);
    }

    [Fact]
    public void OnTheEmoteButtonArtShowsInTheIconAndTheButtonDrawsNoFrame()
    {
        InstallTheme();
        var button = new UIButton();
        var icon = new StatusIcon();
        var reads = 0;

        icon.ShowOn(button, SocialStatus.Awake, () => { reads++; return null; });

        Assert.True(icon.Visible);
        Assert.Null(button.NormalTexture);
        Assert.Null(button.SelectedTexture);
        Assert.Null(button.HoverTexture);
        Assert.Null(button.PressedTexture);
        Assert.Equal(0, reads);
    }

    [Fact]
    public void OnTheEmoteButtonWithNoArtTheIconHidesAndTheLegacyFrameIsRead()
    {
        InstallTheme();
        BuiltInArt.SetEnabledForTests(false);
        var button = new UIButton();
        var icon = new StatusIcon();
        var reads = 0;

        icon.ShowOn(button, SocialStatus.NeedHelp, () => { reads++; return (Texture2D?)null; });

        Assert.False(icon.Visible);
        Assert.Equal(1, reads);
    }

    //a theme with a part for awake only
    private void InstallTheme()
    {
        Theme = UiThemeTests.BuildPack(
            """{ "parts": { "icon.status.awake": { "file": "awake.png" } } }""",
            ("awake.png", UiThemeTests.Png(8, 8)));
        AssetPackRegistry.SetUiThemeForTests(Theme);
    }
}
