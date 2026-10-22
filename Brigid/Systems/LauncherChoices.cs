namespace Brigid.Systems;

/// <summary>
///     The settings that the player chose in the launcher at startup, before <see cref="ClientSettings.Load" />. The
///     launcher cannot save them, so <c>ChaosGame.FinishAssetInitialization</c> hands them to
///     <see cref="ClientSettings.Apply" /> after the settings load.
/// </summary>
/// <param name="UiTheme">The chosen theme (null for Automatic); see <see cref="ClientSettings.UiTheme" />.</param>
public sealed record LauncherChoices(string? UiTheme);
