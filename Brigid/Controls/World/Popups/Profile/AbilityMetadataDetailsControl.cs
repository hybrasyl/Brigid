#region
using Brigid.Collections;
using Brigid.Controls.Components;
using Brigid.Controls.Generic;
using Brigid.Data.Models;
using Brigid.Rendering;
using Brigid.Systems;
using Microsoft.Xna.Framework;
#endregion

namespace Brigid.Controls.World.Popups.Profile;

/// <summary>
///     The detail popup for a skill or spell entry in the status book (BRIG-122): the icon, the level line, the
///     prerequisites, the five stat requirements with their captions, and the description. A requirement that the player
///     meets is white, and one that the player does not meet is red; these colours carry meaning, so they stay literal.
/// </summary>
public sealed class AbilityMetadataDetailsControl : StatusBookDetailsPanel
{
    private const int STAT_COUNT = 5;

    private readonly UILabel LevelLabel;
    private readonly UILabel PreReqCaption;
    private readonly UILabel PreReq1Label;
    private readonly UILabel PreReq2Label;
    private readonly UILabel[] StatCaptions;
    private readonly UILabel[] StatValues;

    public AbilityMetadataDetailsControl()
        : base("abilitydetails", 3, ROW_H)
    {
        //each label is made here with a literal name, so that the primitives report can read it
        LevelLabel = AddRow(new UILabel { Name = "level" });
        PreReqCaption = AddCaption(new UILabel { Name = "prereq_caption", Text = "Requires" });
        PreReq1Label = AddRow(new UILabel { Name = "prereq1" });
        PreReq2Label = AddRow(new UILabel { Name = "prereq2" });

        StatCaptions =
        [
            AddCaption(new UILabel { Name = "str_caption", Text = "STR" }),
            AddCaption(new UILabel { Name = "int_caption", Text = "INT" }),
            AddCaption(new UILabel { Name = "wis_caption", Text = "WIS" }),
            AddCaption(new UILabel { Name = "con_caption", Text = "CON" }),
            AddCaption(new UILabel { Name = "dex_caption", Text = "DEX" })
        ];

        StatValues =
        [
            AddRow(new UILabel { Name = "str" }),
            AddRow(new UILabel { Name = "int" }),
            AddRow(new UILabel { Name = "wis" }),
            AddRow(new UILabel { Name = "con" }),
            AddRow(new UILabel { Name = "dex" })
        ];

        LayoutContent();
    }

    //the level line and the prerequisites beside the icon, and the five stats in a row below it
    protected override void LayoutContent()
    {
        base.LayoutContent();

        var requiresW = CaptionWidth(PreReqCaption);

        PlaceRow(LevelLabel, HeaderX, HeaderY, HeaderWidth);
        PlaceRow(PreReqCaption, HeaderX, HeaderY + ROW_H, requiresW);
        PlaceRow(PreReq1Label, HeaderX + requiresW, HeaderY + ROW_H, HeaderWidth - requiresW);
        PlaceRow(PreReq2Label, HeaderX + requiresW, HeaderY + 2 * ROW_H, HeaderWidth - requiresW);

        var content = ContentBounds;
        var cellW = content.Width / STAT_COUNT;
        var captionW = CaptionWidth(StatCaptions);

        for (var i = 0; i < STAT_COUNT; i++)
        {
            var cellX = content.X + i * cellW;

            PlaceRow(StatCaptions[i], cellX, BodyY, captionW);
            PlaceRow(StatValues[i], cellX + captionW, BodyY, cellW - captionW);
        }
    }

    /// <summary>Shows the popup for <paramref name="entry" />, centered in <paramref name="viewport" />.</summary>
    public void ShowEntry(AbilityMetadataEntry entry, Rectangle viewport)
    {
        Populate(entry);

        var icon = AbilityRequirements.ResolveIcon(entry);
        Icon.Texture = icon.Texture;
        Icon.TextureOffset = new Vector2(icon.OffsetX, icon.OffsetY);

        ShowEntry(entry.Name, entry.Description, viewport);
    }

    /// <summary>Sets the rows and their requirement colours for <paramref name="entry" />. Internal for the tests.</summary>
    internal void Populate(AbilityMetadataEntry entry)
    {
        var attrs = WorldState.Attributes.Current;

        if (entry.RequiresMaster)
        {
            LevelLabel.Text = "master";
            ShowRequirement(LevelLabel, WorldState.IsMaster);
        } else if (entry.AbilityLevel > 0)
        {
            LevelLabel.Text = $"ability {entry.AbilityLevel}";
            ShowRequirement(LevelLabel, AbilityRequirements.IsMet(entry.AbilityLevel, attrs?.Ability));
        } else
        {
            LevelLabel.Text = $"level {entry.Level}";
            ShowRequirement(LevelLabel, AbilityRequirements.IsMet(entry.Level, attrs?.Level));
        }

        SetPreReq(PreReq1Label, entry.PreReq1Name, entry.PreReq1Level);
        SetPreReq(PreReq2Label, entry.PreReq2Name, entry.PreReq2Level);
        PreReqCaption.Visible = (PreReq1Label.Text.Length > 0) || (PreReq2Label.Text.Length > 0);

        SetStat(0, entry.Str, attrs?.Str);
        SetStat(1, entry.Int, attrs?.Int);
        SetStat(2, entry.Wis, attrs?.Wis);
        SetStat(3, entry.Con, attrs?.Con);
        SetStat(4, entry.Dex, attrs?.Dex);
    }

    private static void SetPreReq(UILabel label, string? name, byte level)
    {
        label.Text = AbilityRequirements.FormatPreReq(name, level);
        ShowRequirement(label, AbilityRequirements.HasPreRequisite(name, level));
    }

    private void SetStat(int index, int required, int? current)
    {
        StatValues[index].Text = required.ToString();
        ShowRequirement(StatValues[index], AbilityRequirements.IsMet(required, current));
    }

    //a met requirement is ordinary text, so it takes the theme's text colour; only an unmet one carries meaning
    private static void ShowRequirement(UILabel label, bool met)
    {
        if (met)
            label.SetForegroundRole(ThemeRoles.Text);
        else
            label.ForegroundColor = DialogPalette.RequirementUnmet;
    }
}
