namespace Brigid.ViewModel;

/// <summary>
///     Unified source of truth for all 13 settings in the F4 settings panel. Settings 0-5 and 7 are server-controlled
///     (toggled via opcode 0x1B, names/values set by server response). Settings 6 and 8-12 are client-local (persisted to
///     Darkages.cfg). Uses 0-based indexing matching the UI layout.
/// </summary>
public sealed class UserOptions
{
    public const int SETTING_COUNT = 13;

    private static readonly bool[] ServerSettings =
    [
        true,
        true,
        true,
        true,
        true,
        true, //0-5
        false, //6
        true, //7
        false,
        false,
        false,
        false,
        false //8-12
    ];

    private readonly bool[] Settings = new bool[SETTING_COUNT];

    public static bool IsServerSetting(int index) => index is >= 0 and < SETTING_COUNT && ServerSettings[index];

    public bool this[int index] => index is >= 0 and < SETTING_COUNT && Settings[index];

    /// <summary>
    ///     Fires on any value change (server response or client toggle). Used by UI to refresh labels.
    /// </summary>
    public event UserOptionChangedHandler? SettingChanged;

    /// <summary>
    ///     Fires only on user-initiated toggles. Used by WorldScreen to route to server or persist locally.
    /// </summary>
    public event UserOptionChangedHandler? SettingToggled;

    /// <summary>
    ///     Sets a value and fires <see cref="SettingChanged" />. Used for server responses and initialization.
    /// </summary>
    public void SetValue(int index, bool value)
    {
        if (index is < 0 or >= SETTING_COUNT)
            return;

        Settings[index] = value;
        SettingChanged?.Invoke(index, value);
    }

    /// <summary>
    ///     Handles a user-initiated button click. For client settings, toggles the value and fires both events. For server
    ///     settings, only fires <see cref="SettingToggled" /> — the value updates when the server responds.
    /// </summary>
    public void Toggle(int index)
    {
        if (index is < 0 or >= SETTING_COUNT)
            return;

        if (IsServerSetting(index))
        {
            SettingToggled?.Invoke(index, !Settings[index]);

            return;
        }

        Settings[index] = !Settings[index];
        SettingChanged?.Invoke(index, Settings[index]);
        SettingToggled?.Invoke(index, Settings[index]);
    }

    /// <summary>
    ///     Clears only server-controlled settings, leaving client-local settings intact.
    /// </summary>
    public void ClearServerSettings()
    {
        for (var i = 0; i < SETTING_COUNT; i++)
            if (IsServerSetting(i))
                Settings[i] = false;
    }
}