#region
using Brigid.Collections;
using Brigid.Controls.Components;
using Brigid.Controls.Generic;
using Brigid.Data.Models;
using Brigid.Extensions;
using Brigid.Rendering;
using Brigid.Systems;
using Brigid.ViewModel;
using Chaos.Extensions.Common;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
#endregion

namespace Brigid.Controls.World.Popups.Profile;

/// <summary>
///     Detail popup for a skill/spell entry (_nui_ske prefab). Shows icon, name, level, stat requirements, prerequisites,
///     and description. Dismisses on any click or Escape.
/// </summary>
public sealed class AbilityMetadataDetailsControl : PrefabPanel
{
    private readonly UILabel? ConLabel;
    private readonly UILabel? DescLabel;
    private readonly UILabel? DexLabel;
    private readonly UIImage? IconImage;
    private readonly UILabel? IntLabel;
    private readonly UILabel? LevelLabel;
    private readonly UILabel? NameLabel;
    private readonly UILabel? StrLabel;
    private readonly UILabel? Sub1Label;
    private readonly UILabel? Sub2Label;
    private readonly UILabel? WisLabel;

    public AbilityMetadataDetailsControl()
        : base("_nui_ske")
    {
        Visible = false;
        IsModal = true;
        UsesControlStack = true;

        IconImage = CreateImage("ICON");
        LevelLabel = CreateLabel("LEV");
        LevelLabel?.ForegroundColor = LegendColors.White;
        StrLabel = CreateLabel("STR");
        StrLabel?.ForegroundColor = LegendColors.White;
        StrLabel?.PaddingLeft = 0;
        StrLabel?.PaddingRight = 0;
        
        IntLabel = CreateLabel("INT");
        IntLabel?.ForegroundColor = LegendColors.White;
        IntLabel?.PaddingLeft = 0;
        IntLabel?.PaddingRight = 0;
        
        WisLabel = CreateLabel("WIS");
        WisLabel?.ForegroundColor = LegendColors.White;
        WisLabel?.PaddingLeft = 0;
        WisLabel?.PaddingRight = 0;
        
        ConLabel = CreateLabel("CON");
        ConLabel?.ForegroundColor = LegendColors.White;
        ConLabel?.PaddingLeft = 0;
        ConLabel?.PaddingRight = 0;
        
        DexLabel = CreateLabel("DEX");
        DexLabel?.ForegroundColor = LegendColors.White;
        DexLabel?.PaddingLeft = 0;
        DexLabel?.PaddingRight = 0;
        
        NameLabel = CreateLabel("NAME");
        NameLabel?.ForegroundColor = LegendColors.White;
        Sub1Label = CreateLabel("SUB1");
        Sub1Label?.ForegroundColor = LegendColors.White;
        Sub2Label = CreateLabel("SUB2");
        Sub2Label?.ForegroundColor = LegendColors.White;
        DescLabel = CreateLabel("DESC");
        DescLabel?.ForegroundColor = LegendColors.White;
        DescLabel?.WordWrap = true;
    }

    /// <summary>
    ///     Populates and shows the detail view for the given ability entry.
    /// </summary>
    public void ShowEntry(AbilityMetadataEntry entry, Rectangle viewport)
    {
        this.CenterIn(viewport);

        var attrs = WorldState.Attributes.Current;

        NameLabel?.Text = entry.Name;

        if (LevelLabel is not null)
        {
            if (entry.RequiresMaster)
            {
                LevelLabel.Text = "master";
                LevelLabel.ForegroundColor = WorldState.IsMaster ? LegendColors.White : DialogPalette.RequirementUnmet;
            } else if (entry.AbilityLevel > 0)
            {
                LevelLabel.Text = $"ability {entry.AbilityLevel}";
                LevelLabel.ForegroundColor = AbilityRequirements.RequirementColor(entry.AbilityLevel, attrs?.Ability);
            } else
            {
                LevelLabel.Text = $"level {entry.Level}";
                LevelLabel.ForegroundColor = AbilityRequirements.RequirementColor(entry.Level, attrs?.Level);
            }
        }

        if (StrLabel is not null)
        {
            StrLabel.Text = entry.Str.ToString();
            StrLabel.ForegroundColor = AbilityRequirements.RequirementColor(entry.Str, attrs?.Str);
        }

        if (IntLabel is not null)
        {
            IntLabel.Text = entry.Int.ToString();
            IntLabel.ForegroundColor = AbilityRequirements.RequirementColor(entry.Int, attrs?.Int);
        }

        if (WisLabel is not null)
        {
            WisLabel.Text = entry.Wis.ToString();
            WisLabel.ForegroundColor = AbilityRequirements.RequirementColor(entry.Wis, attrs?.Wis);
        }

        if (ConLabel is not null)
        {
            ConLabel.Text = entry.Con.ToString();
            ConLabel.ForegroundColor = AbilityRequirements.RequirementColor(entry.Con, attrs?.Con);
        }

        if (DexLabel is not null)
        {
            DexLabel.Text = entry.Dex.ToString();
            DexLabel.ForegroundColor = AbilityRequirements.RequirementColor(entry.Dex, attrs?.Dex);
        }

        if (Sub1Label is not null)
        {
            Sub1Label.Text = AbilityRequirements.FormatPreReq(entry.PreReq1Name, entry.PreReq1Level);
            Sub1Label.ForegroundColor = AbilityRequirements.RequirementColor(AbilityRequirements.HasPreRequisite(entry.PreReq1Name, entry.PreReq1Level));
        }

        if (Sub2Label is not null)
        {
            Sub2Label.Text = AbilityRequirements.FormatPreReq(entry.PreReq2Name, entry.PreReq2Level);
            Sub2Label.ForegroundColor = AbilityRequirements.RequirementColor(AbilityRequirements.HasPreRequisite(entry.PreReq2Name, entry.PreReq2Level));
        }

        DescLabel?.Text = entry.Description;

        if (IconImage is not null)
        {
            var icon = AbilityRequirements.ResolveIcon(entry);
            IconImage.Texture = icon.Texture;
            IconImage.TextureOffset = new Vector2(icon.OffsetX, icon.OffsetY);
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