#region
using Brigid.Collections;
using Brigid.Controls.Generic;
using Brigid.Data.Models;
using Brigid.ViewModel;
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
    /// <summary>True when the player's <paramref name="current" /> value meets <paramref name="required" />.</summary>
    public static bool IsMet(int required, int? current) => current >= required;

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

        return (WorldState.SpellBook.FindSlot(name) is { } spell && (WorldState.SpellBook.GetSlot(spell).CurrentLevel >= requiredLevel))
               || (WorldState.SkillBook.FindSlot(name) is { } skill && (WorldState.SkillBook.GetSlot(skill).CurrentLevel >= requiredLevel));
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
        var known = entry.IsSpell ? WorldState.SpellBook.FindSlot(entry.Name) : WorldState.SkillBook.FindSlot(entry.Name);

        return known is null ? UnlearnedIconState(entry) : AbilityIconState.Known;
    }

    /// <summary>
    ///     Whether an ability that the player does not know is learnable or gated by an unmet requirement. A caller that
    ///     already knows that the book does not have the ability (the Abilities window) uses it in place of
    ///     <see cref="ResolveIconState" />, so it does not search the book again.
    /// </summary>
    public static AbilityIconState UnlearnedIconState(AbilityMetadataEntry entry)
    {
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
