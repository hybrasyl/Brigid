#region
using Brigid.Collections;
using Brigid.Controls.Components;
using Brigid.Data.Models;
using Brigid.Extensions;
using Brigid.Rendering;
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
    private static readonly Color UnmetColor = LegendColors.Scarlet;
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

    private static string FormatPreReq(string? name, byte level)
    {
        if (name is null)
            return string.Empty;

        return $"{name} {level}";
    }

    private static bool HasPreRequisite(string? name, byte requiredLevel)
    {
        if (name is null)
            return true;

        for (byte i = 1; i <= SpellBook.MAX_SLOTS; i++)
        {
            ref readonly var slot = ref WorldState.SpellBook.GetSlot(i);

            if (slot.IsOccupied && (slot.AbilityName?.EqualsI(name) == true) && (slot.CurrentLevel >= requiredLevel))
                return true;
        }

        for (byte i = 1; i <= SkillBook.MAX_SLOTS; i++)
        {
            ref readonly var slot = ref WorldState.SkillBook.GetSlot(i);

            if (slot.IsOccupied && (slot.AbilityName?.EqualsI(name) == true) && (slot.CurrentLevel >= requiredLevel))
                return true;
        }

        return false;
    }

    private static IconTexture ResolveIcon(AbilityMetadataEntry entry)
    {
        var renderer = UiRenderer.Instance!;
        var state = ResolveIconState(entry);
        var baseIcon = entry.IsSpell ? renderer.GetSpellIcon(entry.IconSprite) : renderer.GetSkillIcon(entry.IconSprite);

        if (state == AbilityIconState.Known)
            return baseIcon;

        //duotone treatment: luminance × tint. Replaces hue entirely while preserving shape/detail — much more
        //identifiable as a state overlay than a 50/50 blend on colorful modern icons.
        var tint = state == AbilityIconState.Learnable ? LegendColors.CornflowerBlue : LegendColors.DimGray;
        var prefix = entry.IsSpell ? "spell" : "skill";
        var tintedKey = $"{state}:{prefix}:{entry.IconSprite}";
        var tintedTexture = renderer.GetDuotoneTintedTexture(tintedKey, baseIcon.Texture, tint);

        return new IconTexture(tintedTexture, baseIcon.OffsetX, baseIcon.OffsetY);
    }

    private static AbilityIconState ResolveIconState(AbilityMetadataEntry entry)
    {
        if (entry.IsSpell)
            for (byte i = 1; i <= SpellBook.MAX_SLOTS; i++)
            {
                ref readonly var slot = ref WorldState.SpellBook.GetSlot(i);

                if (slot.IsOccupied && (slot.AbilityName?.EqualsI(entry.Name) == true))
                    return AbilityIconState.Known;
            }
        else
            for (byte i = 1; i <= SkillBook.MAX_SLOTS; i++)
            {
                ref readonly var slot = ref WorldState.SkillBook.GetSlot(i);

                if (slot.IsOccupied && (slot.AbilityName?.EqualsI(entry.Name) == true))
                    return AbilityIconState.Known;
            }

        if (WorldState.Attributes.Current is not { } attrs)
            return AbilityIconState.Locked;

        if (entry.RequiresMaster && !WorldState.IsMaster)
            return AbilityIconState.Locked;

        if ((entry.AbilityLevel > 0) && (attrs.Ability < entry.AbilityLevel))
            return AbilityIconState.Locked;

        if (!entry.RequiresMaster && (entry.AbilityLevel == 0) && (attrs.Level < entry.Level))
            return AbilityIconState.Locked;

        if ((attrs.Str < entry.Str)
            || (attrs.Int < entry.Int)
            || (attrs.Wis < entry.Wis)
            || (attrs.Dex < entry.Dex)
            || (attrs.Con < entry.Con))
            return AbilityIconState.Locked;

        if (!HasPreRequisite(entry.PreReq1Name, entry.PreReq1Level))
            return AbilityIconState.Locked;

        if (!HasPreRequisite(entry.PreReq2Name, entry.PreReq2Level))
            return AbilityIconState.Locked;

        return AbilityIconState.Learnable;
    }

    private static Color RequirementColor(int required, int? current) => current >= required ? LegendColors.White : UnmetColor;

    private static Color RequirementColor(bool met) => met ? LegendColors.White : UnmetColor;

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
                LevelLabel.ForegroundColor = WorldState.IsMaster ? LegendColors.White : UnmetColor;
            } else if (entry.AbilityLevel > 0)
            {
                LevelLabel.Text = $"ability {entry.AbilityLevel}";
                LevelLabel.ForegroundColor = RequirementColor(entry.AbilityLevel, attrs?.Ability);
            } else
            {
                LevelLabel.Text = $"level {entry.Level}";
                LevelLabel.ForegroundColor = RequirementColor(entry.Level, attrs?.Level);
            }
        }

        if (StrLabel is not null)
        {
            StrLabel.Text = entry.Str.ToString();
            StrLabel.ForegroundColor = RequirementColor(entry.Str, attrs?.Str);
        }

        if (IntLabel is not null)
        {
            IntLabel.Text = entry.Int.ToString();
            IntLabel.ForegroundColor = RequirementColor(entry.Int, attrs?.Int);
        }

        if (WisLabel is not null)
        {
            WisLabel.Text = entry.Wis.ToString();
            WisLabel.ForegroundColor = RequirementColor(entry.Wis, attrs?.Wis);
        }

        if (ConLabel is not null)
        {
            ConLabel.Text = entry.Con.ToString();
            ConLabel.ForegroundColor = RequirementColor(entry.Con, attrs?.Con);
        }

        if (DexLabel is not null)
        {
            DexLabel.Text = entry.Dex.ToString();
            DexLabel.ForegroundColor = RequirementColor(entry.Dex, attrs?.Dex);
        }

        if (Sub1Label is not null)
        {
            Sub1Label.Text = FormatPreReq(entry.PreReq1Name, entry.PreReq1Level);
            Sub1Label.ForegroundColor = RequirementColor(HasPreRequisite(entry.PreReq1Name, entry.PreReq1Level));
        }

        if (Sub2Label is not null)
        {
            Sub2Label.Text = FormatPreReq(entry.PreReq2Name, entry.PreReq2Level);
            Sub2Label.ForegroundColor = RequirementColor(HasPreRequisite(entry.PreReq2Name, entry.PreReq2Level));
        }

        DescLabel?.Text = entry.Description;

        if (IconImage is not null)
        {
            var icon = ResolveIcon(entry);
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