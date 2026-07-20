#region
using Brigid.Collections;
using Brigid.Controls.Generic;
using Brigid.Data.Models;
using Brigid.ViewModel;
using Chaos.Extensions.Common;
using Microsoft.Xna.Framework;
#endregion

namespace Brigid.Systems;

/// <summary>
///     Shared read-only evaluation of an <see cref="AbilityMetadataEntry" /> against the player's live state —
///     requirement colouring, prerequisite lookup, and the duotone icon treatment. One implementation so every
///     surface that renders an ability (the status-book grid and detail popup, the castable popup's Details tab)
///     agrees on what "met" means and tints an icon identically.
/// </summary>
internal static class AbilityRequirements
{
    /// <summary>White when the player's <paramref name="current" /> value meets <paramref name="required" />, else scarlet.</summary>
    public static Color RequirementColor(int required, int? current)
        => current >= required ? LegendColors.White : DialogPalette.RequirementUnmet;

    /// <summary>White when the requirement is met, else scarlet.</summary>
    public static Color RequirementColor(bool met) => met ? LegendColors.White : DialogPalette.RequirementUnmet;

    /// <summary>"{name} {level}", or empty when there is no prerequisite.</summary>
    public static string FormatPreReq(string? name, byte level) => name is null ? string.Empty : $"{name} {level}";

    /// <summary>
    ///     Whether the player knows <paramref name="name" /> at <paramref name="requiredLevel" /> or better. A null
    ///     name means "no prerequisite", which is trivially satisfied.
    /// </summary>
    public static bool HasPreRequisite(string? name, byte requiredLevel)
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

    /// <summary>The ability's icon, duotone-tinted when it is not already known (blue = learnable, grey = locked).</summary>
    public static IconTexture ResolveIcon(AbilityMetadataEntry entry) => ResolveIcon(entry, ResolveIconState(entry));

    /// <summary>As <see cref="ResolveIcon(AbilityMetadataEntry)" />, for a caller that already computed the state.</summary>
    public static IconTexture ResolveIcon(AbilityMetadataEntry entry, AbilityIconState state)
    {
        var renderer = UiRenderer.Instance!;
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

    /// <summary>Whether the ability is already known, currently learnable, or gated by an unmet requirement.</summary>
    public static AbilityIconState ResolveIconState(AbilityMetadataEntry entry)
    {
        if (entry.IsSpell)
        {
            for (byte i = 1; i <= SpellBook.MAX_SLOTS; i++)
            {
                ref readonly var slot = ref WorldState.SpellBook.GetSlot(i);

                if (slot.IsOccupied && (slot.AbilityName?.EqualsI(entry.Name) == true))
                    return AbilityIconState.Known;
            }
        } else
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
}
