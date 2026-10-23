#region
using Brigid.Controls.Components;
using Brigid.Controls.Generic;
using Brigid.Data.AssetPacks;
using Brigid.Definitions;
using System.Reflection;
using Brigid.Rendering;
using Microsoft.Xna.Framework.Input;
using Xunit;
#endregion

namespace Brigid.Tests;

/// <summary>
///     The BRIG-122 primitive gallery: Toggle opens it on a root and closes it again, Escape closes an open list first and
///     then the gallery, the debug overlay layers are off while it is open and come back as they were when it closes, every child has a lowercase name, every sample lies inside the frame (flat and with a wide themed
///     frame), and every theme role is named on the gallery (the status icon roles by their common prefix).
/// </summary>
[Collection("FontEngine")]
public sealed class PrimitiveGalleryTests : IDisposable
{
    public PrimitiveGalleryTests() => FontEngine.Initialize(0);

    public void Dispose()
    {
        AssetPackRegistry.SetUiThemeForTests(null);
        DebugOverlay.Layers = (true, true, true, true);
    }

    private static UIPanel Root() => new() { Width = 640, Height = 480 };

    [Fact]
    public void ToggleOpensAndClosesTheGallery()
    {
        var root = Root();

        PrimitiveGallery.Toggle(root);
        var gallery = root.FindChild<PrimitiveGallery>("primitivegallery");

        Assert.NotNull(gallery);
        Assert.True(gallery.Visible);

        PrimitiveGallery.Toggle(root);

        Assert.Null(root.FindChild<PrimitiveGallery>("primitivegallery"));
    }

    [Fact]
    public void EscapeClosesAndRemovesTheGallery()
    {
        var root = Root();
        PrimitiveGallery.Toggle(root);

        root.FindChild<PrimitiveGallery>("primitivegallery")!.OnKeyDown(new KeyDownEvent { Key = Keys.Escape });

        Assert.Null(root.FindChild<PrimitiveGallery>("primitivegallery"));
    }

    [Fact]
    public void OpenTurnsTheOverlayLayersOffAndCloseRestoresThem()
    {
        var root = Root();
        DebugOverlay.Layers = (true, false, true, true);

        PrimitiveGallery.Toggle(root);

        Assert.Equal((false, false, false, false), DebugOverlay.Layers);

        //a layer turned on while the gallery is open does not stay on after it closes
        DebugOverlay.ShowUiNames = true;
        PrimitiveGallery.Toggle(root);

        Assert.Equal((true, false, true, true), DebugOverlay.Layers);
    }

    [Fact]
    public void EscapeRestoresTheOverlayLayers()
    {
        var root = Root();
        DebugOverlay.Layers = (true, true, false, true);
        PrimitiveGallery.Toggle(root);

        root.FindChild<PrimitiveGallery>("primitivegallery")!.OnKeyDown(new KeyDownEvent { Key = Keys.Escape });

        Assert.Equal((true, true, false, true), DebugOverlay.Layers);
    }

    [Fact]
    public void DisposingTheRootRestoresTheOverlayLayers()
    {
        var root = Root();
        DebugOverlay.Layers = (false, true, true, false);
        PrimitiveGallery.Toggle(root);

        //a screen change disposes its root, and the gallery with it, without a close
        root.Dispose();

        Assert.Equal((false, true, true, false), DebugOverlay.Layers);
    }

    [Fact]
    public void ToggleWithNoRootDoesNothing() => PrimitiveGallery.Toggle(null);

    [Fact]
    public void EveryChildHasAUniqueName()
    {
        var root = Root();
        PrimitiveGallery.Toggle(root);

        UiAssert.NamedTree(root.FindChild<PrimitiveGallery>("primitivegallery")!);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EverySampleLiesInsideTheFrame(bool themed)
    {
        using var theme = UiThemeTests.BuildPack(
            """{ "density": 4, "parts": { "frame.panel": { "file": "frame.png", "slice": [44, 44, 44, 44] }, "frame.popup": { "file": "frame.png", "slice": [12, 12, 12, 12] } } }""",
            ("frame.png", UiThemeTests.Png(128, 128)));
        AssetPackRegistry.SetUiThemeForTests(themed ? theme : null);

        var root = Root();
        PrimitiveGallery.Toggle(root);
        var gallery = root.FindChild<PrimitiveGallery>("primitivegallery")!;
        var content = gallery.ContentBounds;

        foreach (var child in gallery.Children)
        {
            if ((child.Name == "title") || (child is TextButton && (child.Y >= gallery.BottomBarTop)))
                continue;

            Assert.True(child.X >= content.X, $"{child.Name} starts left of the content");
            Assert.True(child.Y >= content.Y, $"{child.Name} starts above the content");
            Assert.True(child.X + child.Width <= content.Right, $"{child.Name} ends right of the content ({child.X + child.Width} > {content.Right})");
            Assert.True(child.Y + child.Height <= content.Bottom, $"{child.Name} ends below the content ({child.Y + child.Height} > {content.Bottom})");
        }

        Assert.True(gallery.Width <= 640 && gallery.Height <= 480);

        //the popup menu sample, open, stays inside the gallery
        gallery.FindChild<TextButton>("popup_open")!.OnClick(new ClickEvent { Button = MouseButton.Left });
        var menu = gallery.FindChild<PopupMenu>("popup_menu")!;

        Assert.True(menu.Visible);
        Assert.True(menu.X >= 0 && menu.Y >= 0 && menu.X + menu.Width <= gallery.Width && menu.Y + menu.Height <= gallery.Height);
    }

    [Fact]
    public void EveryThemeRoleIsNamedOnTheGallery()
    {
        var root = Root();
        PrimitiveGallery.Toggle(root);
        var gallery = root.FindChild<PrimitiveGallery>("primitivegallery")!;
        var texts = string.Join(" ", AllLabels(gallery).Select(label => label.Text));

        var roles = typeof(ThemeRoles)
                    .GetFields(BindingFlags.Public | BindingFlags.Static)
                    .Where(field => field.IsLiteral)
                    .Select(field => (string)field.GetRawConstantValue()!)
                    .Except(ThemeRoles.StatusIcons);

        Assert.All(roles, role => Assert.Contains(role, texts));

        //the status row names its roles once, as the prefix and the part after it
        var statusRoles = gallery.FindChild<UILabel>("status_roles")!.Text;
        Assert.Contains("icon.status.*", statusRoles);
        Assert.All(ThemeRoles.StatusIcons, role => Assert.Contains(role["icon.status.".Length..], statusRoles));
    }

    //BRIG-135: the open list is on the control stack, so it gets Escape before the gallery
    [Fact]
    public void EscapeClosesAnOpenListFirst()
    {
        var dispatcher = new InputDispatcher();
        var root = Root();

        try
        {
            PrimitiveGallery.Toggle(root);
            var gallery = root.FindChild<PrimitiveGallery>("primitivegallery")!;
            var dropDown = gallery.FindChild<DropDown>("dropdown")!;
            dropDown.Open();

            dispatcher.DispatchKeyboardEvent(root, new KeyDownEvent { Key = Keys.Escape });

            Assert.False(dropDown.IsOpen);
            Assert.True(gallery.Visible);

            dispatcher.DispatchKeyboardEvent(root, new KeyDownEvent { Key = Keys.Escape });

            Assert.False(gallery.Visible);
        } finally
        {
            if (root.FindChild<PrimitiveGallery>("primitivegallery") is { Visible: true })
                PrimitiveGallery.Toggle(root);

            dispatcher.Dispose();
        }
    }

    [Fact]
    public void CloseTakesTheFocusFromTheLiveField()
    {
        var root = Root();
        PrimitiveGallery.Toggle(root);
        var field = root.FindChild<UITextBox>("live_field")!;
        field.IsFocused = true;

        PrimitiveGallery.Toggle(root);

        Assert.False(field.IsFocused);
        Assert.Null(UITextBox.CurrentlyFocused);
    }

    [Fact]
    public void TheStateHooksTakeTheSampleOffTheMouse()
    {
        var button = new TextButton("Hover", 70, 20);
        var box = new CheckBox();

        button.ShowState(true, false);
        box.ShowState(true);

        Assert.False(button.IsHitTestVisible);
        Assert.False(box.IsHitTestVisible);
    }

    private static IEnumerable<UILabel> AllLabels(UIPanel panel)
    {
        foreach (var child in panel.Children)
        {
            if (child is UILabel label)
                yield return label;

            if (child is UIPanel inner)
                foreach (var nested in AllLabels(inner))
                    yield return nested;
        }
    }
}
