#region
using Brigid.Controls.Components;
using Brigid.Data.Models;
using Brigid.Extensions;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
#endregion

namespace Brigid.Controls.World.Popups.Profile;

/// <summary>
///     Detail popup for an event entry (_nui_eve prefab). Shows state icon from leicon.epf, name, circle info,
///     prerequisites, reward, and description (Result when completed, Summary otherwise). Dismisses on any click or
///     Escape.
/// </summary>
public sealed class EventMetadataDetailsControl : PrefabPanel
{
    private readonly UILabel? DescLabel;
    private readonly UIImage? IconImage;
    private readonly UILabel? LevelLabel;
    private readonly UILabel? MustLabel;
    private readonly UILabel? NameLabel;
    private readonly UILabel? RewardLabel;

    public EventMetadataDetailsControl()
        : base("_nui_eve")
    {
        Visible = false;
        IsModal = true;
        UsesControlStack = true;

        IconImage = CreateImage("ICON");
        NameLabel = CreateLabel("NAME");
        NameLabel?.ForegroundColor = LegendColors.White;
        LevelLabel = CreateLabel("LEV");
        LevelLabel?.ForegroundColor = LegendColors.White;
        LevelLabel?.Width = 150;
        
        MustLabel = CreateLabel("MUST");
        MustLabel?.ForegroundColor = LegendColors.White;
        RewardLabel = CreateLabel("REWARD");
        RewardLabel?.ForegroundColor = LegendColors.White;
        DescLabel = CreateLabel("DESC");
        DescLabel?.ForegroundColor = LegendColors.White;
        DescLabel?.WordWrap = true;
    }

    private static string FormatPrerequisites(EventMetadataEntry entry)
        => !string.IsNullOrEmpty(entry.PreRequisiteId) ? entry.PreRequisiteId : string.Empty;

    /// <summary>
    ///     Populates and shows the detail view for the given event entry.
    /// </summary>
    public void ShowEntry(EventMetadataEntry entry, EventState state, Rectangle viewport)
    {
        this.CenterIn(viewport);

        NameLabel?.Text = entry.Title;

        LevelLabel?.Text = entry.Page >= 6 ? "master" : $"{entry.Page} level range";

        MustLabel?.Text = FormatPrerequisites(entry);

        RewardLabel?.Text = entry.Reward;

        DescLabel?.Text = state == EventState.Completed ? entry.Result : entry.Summary;

        if (IconImage is not null)
        {
            var iconFrame = state switch
            {
                EventState.Completed => 0,
                EventState.Available => 1,
                _                    => 2
            };

            IconImage.Texture = UiRenderer.Instance!.GetEpfTexture("leicon.epf", iconFrame);
        }

        Show();
    }

    public override void OnKeyDown(KeyDownEvent e)
    {
        if (e.Key is Keys.Escape or Keys.Enter)
        {
            Hide();
            e.Handled = true;
        }
    }

    public override void OnClick(ClickEvent e)
    {
        Hide();
        e.Handled = true;
    }
}