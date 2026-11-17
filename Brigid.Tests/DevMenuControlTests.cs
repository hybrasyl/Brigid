#region
using Brigid.Controls.Components;
using Brigid.Controls.Generic;
using Brigid.Definitions;
using Brigid.DevModules;
using Brigid.Rendering;
using Xunit;
#endregion

namespace Brigid.Tests;

/// <summary>
///     The BRIG-29 dev menu: one name row per module and one button per action, a click closes the menu and runs the
///     action, an action that throws goes to the log, and Shift+F11 opens and closes the menu on a root.
/// </summary>
[Collection("FontEngine")]
public sealed class DevMenuControlTests
{
    private readonly List<string> Log = [];

    public DevMenuControlTests() => FontEngine.Initialize(0);

    [Fact]
    public void EveryChildHasAUniqueName()
    {
        var menu = new DevMenuControl([new MenuModule("One", new DevAction("A", () => { }), new DevAction("B", () => { }))], Log.Add);

        Assert.Equal("devmenu", menu.Name);
        UiAssert.NamedTree(menu);
        Assert.Equal("One", menu.FindChild<UILabel>("module_0")!.Text);
        Assert.NotNull(menu.FindChild<TextButton>("action_0_0"));
        Assert.NotNull(menu.FindChild<TextButton>("action_0_1"));
    }

    [Fact]
    public void AClickClosesTheMenuAndRunsTheAction()
    {
        var runs = 0;
        var menu = new DevMenuControl([new MenuModule("One", new DevAction("Run", () => runs++))], Log.Add);
        menu.Show();

        menu.FindChild<TextButton>("action_0_0")!.OnClick(new ClickEvent { Button = MouseButton.Left });

        Assert.Equal(1, runs);
        Assert.False(menu.Visible);
    }

    [Fact]
    public void AnActionThatThrowsGoesToTheLog()
    {
        var menu = new DevMenuControl([new MenuModule("One", new DevAction("Fail", () => throw new InvalidOperationException("no")))], Log.Add);
        menu.Show();

        menu.FindChild<TextButton>("action_0_0")!.OnClick(new ClickEvent { Button = MouseButton.Left });

        var line = Assert.Single(Log);
        Assert.StartsWith("One: Fail failed: System.InvalidOperationException: no", line);
    }

    [Fact]
    public void TheModuleRowShowsTheDescription()
    {
        var menu = new DevMenuControl([new MenuModule("One", "Does a thing.")], Log.Add);

        Assert.Equal("One: Does a thing.", menu.FindChild<UILabel>("module_0")!.Text);
    }

    [Fact]
    public void AModuleWithNoActionsOrANullActionDoesNotStopTheMenu()
    {
        var menu = new DevMenuControl([new NullActionsModule(), new MenuModule("Two", "", null!, new DevAction("Run", () => { }))], Log.Add);

        Assert.NotNull(menu.FindChild<UILabel>("module_0"));
        Assert.NotNull(menu.FindChild<TextButton>("action_1_0"));
        Assert.Null(menu.FindChild<TextButton>("action_1_1"));
    }

    [Fact]
    public void WithNoModuleTheMenuSaysSo()
    {
        var menu = new DevMenuControl([], Log.Add);

        Assert.Equal("No dev module is loaded.", menu.FindChild<UILabel>("empty")!.Text);
    }

    [Fact]
    public void TheRowsFitInThePanel()
    {
        var menu = new DevMenuControl(
            [new MenuModule("One", new DevAction("A", () => { })), new MenuModule("Two", new DevAction("B", () => { }), new DevAction("C", () => { }))],
            Log.Add);
        var content = menu.ContentBounds;

        foreach (var name in (string[]) ["action_0_0", "action_1_0", "action_1_1"])
        {
            var row = menu.FindChild<TextButton>(name)!;

            Assert.True(row.Y >= content.Y && row.Y + row.Height <= content.Bottom, name);
        }
    }

    [Fact]
    public void ToggleOpensTheMenuOnTheRootAndClosesIt()
    {
        var root = new UIPanel { Name = "root", Width = ChaosGame.VIRTUAL_WIDTH, Height = ChaosGame.VIRTUAL_HEIGHT };

        DevMenuControl.Toggle(root, [], Log.Add);
        var menu = root.FindChild<DevMenuControl>("devmenu");

        Assert.NotNull(menu);
        Assert.True(menu.Visible);

        DevMenuControl.Toggle(root, [], Log.Add);

        Assert.Null(root.FindChild<DevMenuControl>("devmenu"));
    }

    [Fact]
    public void ToggleWithNoRootDoesNothing() => DevMenuControl.Toggle(null, [], Log.Add);

    private sealed class MenuModule(string name, string description, params DevAction[] actions) : IDevModule
    {
        public MenuModule(string name, params DevAction[] actions) : this(name, "", actions) { }

        public string Name => name;

        public string Description => description;

        public IReadOnlyList<DevAction> Actions => actions;

        public void Initialize(IDevHost host) { }

        public void Shutdown() { }
    }

    private sealed class NullActionsModule : IDevModule
    {
        public string Name => "Null";

        public string Description => "";

        public IReadOnlyList<DevAction> Actions => null!;

        public void Initialize(IDevHost host) { }

        public void Shutdown() { }
    }
}
