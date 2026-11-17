namespace Brigid.DevModules;

/// <summary>
///     One row of the dev menu. A click runs <paramref name="Invoke" /> on the game thread, and the menu closes. An
///     exception from it goes to the log and does not stop the client.
/// </summary>
public sealed record DevAction(string Label, Action Invoke);
