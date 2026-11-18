using Xunit;

namespace Brigid.SmokeTests;

/// <summary>Spec 1 of BRIG-184: the client starts with the environment set and shows the main menu.</summary>
[Collection("Smoke")]
public sealed class StartupSpecs
{
    [SmokeFact]
    public Task TheClientStartsToTheMainMenu()
        => BrigidApp.RunAsync(
            async app =>
            {
                await app.WaitForScreenAsync("LobbyLoginScreen");
                //the title, not the buttons: with a server, the buttons stay hidden while its greeting shows
                await app.WaitForShownAsync("mainmenu");
                await app.WaitForShownAsync("mainmenu/title");

                //the client uses the test's temporary app folder, not the developer's
                Assert.Equal(Path.GetFullPath(app.Root), await app.AppRootAsync());

                //the temporary assets folder exists and is readable, so the menu has no "No Packs" alert
                Assert.DoesNotContain("No Packs", await app.AlertsAsync());

                //a test client does not call GitHub for the release check
                Assert.Contains(app.OutputLines, line => line.Contains("[update] BRIGID_APP_ROOT is set, so the release check does not run"));
            });
}
