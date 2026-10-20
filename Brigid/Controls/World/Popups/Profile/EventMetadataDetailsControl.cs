#region
using Brigid.Controls.Components;
using Brigid.Data.Models;
using Microsoft.Xna.Framework;
#endregion

namespace Brigid.Controls.World.Popups.Profile;

/// <summary>
///     The detail popup for an event entry in the status book (BRIG-122): the state icon from <c>leicon.epf</c> (game
///     content), the level range, the prerequisite, the reward, and the description (the result when the event is
///     completed, else the summary). A caption hides when its value is empty.
/// </summary>
public sealed class EventMetadataDetailsControl : StatusBookDetailsPanel
{
    private readonly UILabel LevelLabel;
    private readonly UILabel PreReqCaption;
    private readonly UILabel PreReqLabel;
    private readonly UILabel RewardCaption;
    private readonly UILabel RewardLabel;

    public EventMetadataDetailsControl()
        : base("eventdetails", 3, 0)
    {
        //each label is made here with a literal name, so that the primitives report can read it
        LevelLabel = AddRow(new UILabel { Name = "level" });
        PreReqCaption = AddCaption(new UILabel { Name = "prereq_caption", Text = "Requires" });
        PreReqLabel = AddRow(new UILabel { Name = "prereq" });
        RewardCaption = AddCaption(new UILabel { Name = "reward_caption", Text = "Reward" });
        RewardLabel = AddRow(new UILabel { Name = "reward" });

        LayoutContent();
    }

    //the level range, then the prerequisite and the reward with their captions, beside the icon
    protected override void LayoutContent()
    {
        base.LayoutContent();

        var captionW = CaptionWidth(PreReqCaption, RewardCaption);

        PlaceRow(LevelLabel, HeaderX, HeaderY, HeaderWidth);
        PlaceRow(PreReqCaption, HeaderX, HeaderY + ROW_H, captionW);
        PlaceRow(PreReqLabel, HeaderX + captionW, HeaderY + ROW_H, HeaderWidth - captionW);
        PlaceRow(RewardCaption, HeaderX, HeaderY + 2 * ROW_H, captionW);
        PlaceRow(RewardLabel, HeaderX + captionW, HeaderY + 2 * ROW_H, HeaderWidth - captionW);
    }

    /// <summary>Shows the popup for <paramref name="entry" /> in <paramref name="state" />, centered in <paramref name="viewport" />.</summary>
    public void ShowEntry(EventMetadataEntry entry, EventState state, Rectangle viewport)
    {
        Populate(entry);

        var iconFrame = state switch
        {
            EventState.Completed => 0,
            EventState.Available => 1,
            _                    => 2
        };

        Icon.Texture = UiRenderer.Instance!.GetEpfTexture("leicon.epf", iconFrame);
        Icon.TextureOffset = Vector2.Zero;

        ShowEntry(entry.Title, state == EventState.Completed ? entry.Result : entry.Summary, viewport);
    }

    /// <summary>Sets the rows for <paramref name="entry" />. Internal for the tests.</summary>
    internal void Populate(EventMetadataEntry entry)
    {
        LevelLabel.Text = entry.Page >= 6 ? "master" : $"{entry.Page} level range";
        PreReqLabel.Text = entry.PreRequisiteId ?? string.Empty;
        RewardLabel.Text = entry.Reward;
        PreReqCaption.Visible = PreReqLabel.Text.Length > 0;
        RewardCaption.Visible = RewardLabel.Text.Length > 0;
    }
}
