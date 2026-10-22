#region
using Brigid.Controls.Components;
using Brigid.Definitions;
using DALib.Networking.Packets.Server;
#endregion

namespace Brigid.Controls.World.Popups;

/// <summary>
///     The group box viewer (BRIG-126 step 3c): the group box of another player, in the <see cref="GroupRecruitPanel" />
///     form, read-only, in its own themed panel centred in the HUD viewport. Join asks to join the group and closes the
///     panel; Close only closes it. The keys that the panel does not use go on to the screen (D10).
/// </summary>
public sealed class GroupBoxViewerControl : CenteredModalPanel
{
    private const int ACTION_W = 56;

    /// <summary>The recruit form. Internal for the tests.</summary>
    internal GroupRecruitPanel Form { get; }

    /// <summary>Raised when the player clicks Join. The parameter is the name of the player who owns the group box.</summary>
    public event RequestJoinHandler? OnRequestJoin
    {
        add => Form.OnRequestJoin += value;
        remove => Form.OnRequestJoin -= value;
    }

    public GroupBoxViewerControl()
        : base("Group Box", GroupRecruitPanel.FORM_W + 2 * PADDING, TITLE_H + 1 + GroupRecruitPanel.FORM_H + BOTTOM_H, useTheme: true)
    {
        Name = "groupbox";

        var content = ContentBounds;

        Form = new GroupRecruitPanel
        {
            Name = "form",
            X = content.X,
            Y = content.Y
        };
        Form.OnClose += Hide;
        AddChild(Form);

        AddBottomBarButton("Join", ACTION_W, Form.RequestJoin, "join");
    }

    protected override bool LetsKeysThrough => true;

    /// <summary>Shows the group box of <paramref name="sourceName" />.</summary>
    public void ShowAsViewer(string sourceName, GroupRecruitInfo info)
    {
        Form.ShowAsViewer(sourceName, info);
        Show();
    }
}
