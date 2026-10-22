#region
using Brigid.Controls.Generic;
using Brigid.Controls.LobbyLogin;
using Brigid.Rendering;
using Xunit;
#endregion

namespace Brigid.Tests;

/// <summary>
///     The BRIG-107 menu alerts: the panel shows while it has an alert, the selection stays sensible when alerts go, the
///     tab row keeps the selected tab in view, and an action removes its alert before it runs.
/// </summary>
[Collection("FontEngine")]
public class AlertsPanelTests
{
    public AlertsPanelTests() => FontEngine.Initialize(0);

    [Fact]
    public void TheFirstAlertShowsThePanelAndIsSelected()
    {
        var panel = new AlertsPanel();

        Assert.False(panel.Visible);
        Assert.Equal(-1, panel.SelectedIndex);

        panel.Add(Alert("a"));

        Assert.True(panel.Visible);
        Assert.Equal(0, panel.SelectedIndex);
    }

    [Fact]
    public void AnAlertWithTheSameIdReplacesTheShownOne()
    {
        var panel = new AlertsPanel();
        panel.Add(Alert("a"));
        panel.Add(Alert("b"));

        panel.Add(Alert("a", "new title"));

        Assert.Equal(["a", "b"], panel.Alerts.Select(alert => alert.Id));
        Assert.Equal("new title", panel.Alerts[0].Title);
    }

    [Theory]
    //removing the selected alert selects the one that takes its place
    [InlineData(1, "b", 1, "c")]
    //removing an alert before the selected one keeps the same alert selected
    [InlineData(2, "a", 1, "c")]
    //removing the selected last alert selects the new last alert
    [InlineData(2, "c", 1, "b")]
    //removing an alert after the selected one changes nothing
    [InlineData(0, "c", 0, "a")]
    public void RemovingAnAlertKeepsASensibleSelection(int selected, string removed, int expectedIndex, string expectedId)
    {
        var panel = new AlertsPanel();
        panel.Add(Alert("a"));
        panel.Add(Alert("b"));
        panel.Add(Alert("c"));
        panel.Select(selected);

        Assert.True(panel.Remove(removed));

        Assert.Equal(expectedIndex, panel.SelectedIndex);
        Assert.Equal(expectedId, panel.Alerts[panel.SelectedIndex].Id);
    }

    [Fact]
    public void RemovingTheLastAlertHidesThePanel()
    {
        var panel = new AlertsPanel();
        panel.Add(Alert("a"));

        Assert.True(panel.Remove("a"));
        Assert.False(panel.Remove("a"));

        Assert.False(panel.Visible);
        Assert.Equal(-1, panel.SelectedIndex);
    }

    [Fact]
    public void NextGoesRoundToTheFirstAlert()
    {
        var panel = new AlertsPanel();
        panel.Add(Alert("a"));
        panel.Add(Alert("b"));

        panel.SelectNext();
        Assert.Equal(1, panel.SelectedIndex);

        panel.SelectNext();
        Assert.Equal(0, panel.SelectedIndex);
    }

    [Fact]
    public void TheNextArrowShowsOnlyForMoreThanOneAlert()
    {
        var panel = new AlertsPanel();
        var next = panel.FindChild<TextButton>("next")!;

        panel.Add(Alert("a"));
        Assert.False(next.Visible);

        panel.Add(Alert("b"));
        Assert.True(next.Visible);
    }

    [Fact]
    public void TheTabRowKeepsTheSelectedTabInView()
    {
        var panel = new AlertsPanel();
        var count = AlertsPanel.TabCapacity + 2;

        for (var i = 0; i < count; i++)
            panel.Add(Alert($"alert{i}"));

        panel.Select(count - 1);

        var tabs = panel.FindChild<TabStrip>("tabs")!;

        Assert.Equal(AlertsPanel.TabCapacity, tabs.Children.Count);
        Assert.Equal(AlertsPanel.TabCapacity - 1, tabs.SelectedIndex);

        //next from the last alert goes round, and the row moves back to the start
        panel.SelectNext();
        tabs = panel.FindChild<TabStrip>("tabs")!;

        Assert.Equal(0, tabs.SelectedIndex);
    }

    [Fact]
    public void AnActionRemovesItsAlertThenRuns()
    {
        var panel = new AlertsPanel();
        var countWhenRun = -1;

        panel.Add(new MenuAlert("a", "A", "body", [new MenuAlertAction("Go", () => countWhenRun = panel.Alerts.Count)]));
        panel.Add(Alert("b"));

        panel.RunAction(0);

        Assert.Equal(1, countWhenRun);
        Assert.Equal("b", Assert.Single(panel.Alerts).Id);
    }

    [Fact]
    public void AnActionCanShowItsAlertAgain()
    {
        var panel = new AlertsPanel();
        MenuAlert again = null!;
        again = new MenuAlert("a", "A", "body", [new MenuAlertAction("Again", () => panel.Add(again))]);

        panel.Add(again);
        panel.RunAction(0);

        Assert.Equal("a", Assert.Single(panel.Alerts).Id);
        Assert.True(panel.Visible);
    }

    [Fact]
    public void TheButtonsAreTheActionsOfTheSelectedAlert()
    {
        var panel = new AlertsPanel();
        panel.Add(MenuAlerts.NoSound(false));
        panel.Add(MenuAlerts.LegacyImport(() => { }, () => { }));

        Assert.Single(panel.FindChild<Controls.Components.UIPanel>("actions")!.Children);

        panel.Select(1);

        Assert.Equal(2, panel.FindChild<Controls.Components.UIPanel>("actions")!.Children.Count);
    }

    [Fact]
    public void TheLegacyImportActionsRunTheirCallbacks()
    {
        var panel = new AlertsPanel();
        var imported = false;
        var skipped = false;

        panel.Add(MenuAlerts.LegacyImport(() => imported = true, () => skipped = true));
        panel.RunAction(1);

        Assert.False(imported);
        Assert.True(skipped);
        Assert.Empty(panel.Alerts);
    }

    [Fact]
    public void TheOfflineAlertReconnects_AndGoesAway()
    {
        var panel = new AlertsPanel();
        var reconnects = 0;

        panel.Add(MenuAlerts.Offline(() => reconnects++));
        panel.RunAction(0);

        Assert.Equal(1, reconnects);
        Assert.Empty(panel.Alerts);
    }

    //BRIG-129: an assets folder that cannot be listed shows an alert that names the folder
    [Fact]
    public void TheUnreadableAssetsAlertNamesTheFolder_AndDismisses()
    {
        var panel = new AlertsPanel();

        panel.Add(MenuAlerts.AssetsUnreadable(@"C:\packs"));

        var alert = Assert.Single(panel.Alerts);
        Assert.Contains(@"`C:\packs`", alert.Body);

        panel.RunAction(0);

        Assert.Empty(panel.Alerts);
    }

    [Fact]
    public void ASecondOfflineAlertReplacesTheFirst()
    {
        var panel = new AlertsPanel();

        panel.Add(MenuAlerts.Offline(static () => { }));
        panel.Add(MenuAlerts.Offline(static () => { }));

        Assert.Single(panel.Alerts);
        Assert.True(panel.Remove(MenuAlerts.OFFLINE_ID));
    }

    [Fact]
    public void TheInstallCommandsCoverTheThreeDistroFamilies()
        => Assert.Equal(["apt", "dnf", "pacman"], MenuAlerts.InstallCommands.Select(c => c.Command.Split(' ')[1]));

    [Fact]
    public void TheDebianInstallCommandNamesTheMixerPackage()
        => Assert.Contains(MenuAlerts.InstallCommands, c => c.Command == "sudo apt install libsdl2-mixer-2.0-0");

    [Fact]
    public void TheNoSoundAlertAsksForAReinstallOffLinux()
        => Assert.Contains("Install Brigid again", MenuAlerts.NoSound(false).Body);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TheNoSoundAlertGivesTheInstallCommandsOnlyOnLinux(bool linux)
    {
        var body = MenuAlerts.NoSound(linux).Body;

        Assert.All(MenuAlerts.InstallCommands, entry => Assert.Equal(linux, body.Contains(entry.Command)));
    }

    [Fact]
    public void EveryShippedAlertFitsOnEveryFace()
    {
        var engine = FontEngine.Instance;
        var original = engine.ActiveFontIndex;
        var failures = new List<string>();

        try
        {
            for (var face = 0; face < engine.FontCount; face++)
            {
                engine.SetActiveFont(face);

                foreach (var alert in (MenuAlert[])[MenuAlerts.NoSound(true), MenuAlerts.NoSound(false), MenuAlerts.LegacyImport(() => { }, () => { })])
                {
                    var panel = new AlertsPanel();
                    panel.Add(alert);

                    var body = panel.FindChild<MarkdownBlock>("body")!;

                    if (body.ContentHeight > body.Height)
                        failures.Add($"{engine.ActiveFontName}: {alert.Id} needs {body.ContentHeight} px, has {body.Height}");
                }
            }
        } finally
        {
            engine.SetActiveFont(original);
        }

        Assert.Empty(failures);
    }

    [Fact]
    public void ThePanelGrowsUpFromItsBottomEdge()
    {
        var panel = new AlertsPanel { Bottom = 438 };

        panel.Add(Alert("short"));
        var shortHeight = panel.Height;

        panel.Add(new MenuAlert("long", "Long", string.Concat(Enumerable.Repeat("long alert text ", 60)), []));
        panel.Select(1);

        Assert.True(panel.Height > shortHeight);
        Assert.True(panel.Height <= AlertsPanel.MAX_HEIGHT);
        Assert.Equal(438, panel.Y + panel.Height);
    }

    [Fact]
    public void AnAlertWithNoActionsCanStillBeDismissed()
    {
        var panel = new AlertsPanel();

        panel.Add(new MenuAlert("a", "A", "body", []));

        Assert.Single(panel.FindChild<Controls.Components.UIPanel>("actions")!.Children);

        panel.RunAction(0);

        Assert.Empty(panel.Alerts);
    }

    [Fact]
    public void ANewSelectionInsideTheRowKeepsTheTabs()
    {
        var panel = new AlertsPanel();
        panel.Add(Alert("a"));
        panel.Add(Alert("b"));
        var tabs = panel.FindChild<TabStrip>("tabs")!;

        panel.Select(1);

        Assert.Same(tabs, panel.FindChild<TabStrip>("tabs"));
        Assert.Equal(1, tabs.SelectedIndex);
    }

    private static MenuAlert Alert(string id, string? title = null) => new(id, title ?? id, $"body of {id}", [new MenuAlertAction("OK")]);
}
