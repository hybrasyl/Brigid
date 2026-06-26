#region
using Brigid.Controls.Components;
#endregion

namespace Brigid.Controls.World.Popups.Profile;

/// <summary>
///     A generic tab page within the status book. Loaded from a tab-specific prefab (_nui_sk, _nui_dr, etc.).
/// </summary>
public sealed class SelfProfileBlankTab : PrefabPanel
{
    public SelfProfileBlankTab(string prefabName)
        : base(prefabName, false)
    {
        Name = prefabName;
        Visible = false;
    }
}