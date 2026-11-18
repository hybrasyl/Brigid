#region
using Brigid.Controls.Components;
using Brigid.Rendering;
using Chaos.DarkAges.Definitions;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Brigid.Controls.World.Hud.Main;

/// <summary>An effect of the effect bar: its icon and the colour band of its time left (0x3A).</summary>
internal readonly record struct HudEffect(ushort Icon, EffectColor Color);

/// <summary>
///     The effect bar of the new HUD (BRIG-147, BRIG-127 D5, D19): a strip across the top of the bottom area with one slot
///     for each effect that the server sends (0x3A), left to right in the order that they arrive. A removed effect closes
///     the gap; a colour update keeps the place of its effect. The effects after the last slot wait, and show when a slot
///     is free. Each slot shows the half-size spell icon and a clock swipe over the part of the time that has gone; the
///     packet gives that part only in the six steps of its colour bands. The strip draws the
///     <see cref="ThemeRoles.HudEffectBar" /> part, else nothing over the bottom area.
/// </summary>
internal sealed class HudEffectBar : UIPanel
{
    /// <summary>The colour bands of the packet: each is a sixth of the time.</summary>
    public const int BANDS = 6;

    private readonly List<HudEffect> Shown = [];
    private readonly HudEffectSlot[] Slots;
    private readonly ClockWedgeCache Wedges = new(MainHudLayout.EFFECT_ICON);

    /// <param name="iconSource">The half-size icon of an effect; null for the spell icons of <see cref="UiRenderer" />.</param>
    public HudEffectBar(Func<ushort, Texture2D?>? iconSource = null)
    {
        Name = "effect_bar";
        SetBounds(MainHudLayout.EffectBar);
        IsHitTestVisible = false;
        iconSource ??= icon => UiRenderer.Instance?.GetHalfSizeSpellIcon(icon);

        Slots = new HudEffectSlot[MainHudLayout.MaxEffectSlots];

        for (var i = 0; i < Slots.Length; i++)
        {
            Slots[i] = new HudEffectSlot(Wedges, iconSource) { Name = $"effect_{i}" };
            Slots[i].SetBounds(MainHudLayout.EffectSlotAt(i));
            AddChild(Slots[i]);
        }
    }

    /// <summary>The effects, in the order of the bar; the ones past the last slot wait.</summary>
    public IReadOnlyList<HudEffect> Effects => Shown;

    /// <summary>The slots of the bar, from the left.</summary>
    internal IReadOnlyList<HudEffectSlot> SlotList => Slots;

    /// <summary>Shows, changes or removes the effect of <paramref name="icon" />; <see cref="EffectColor.None" /> removes it.</summary>
    public void SetEffect(ushort icon, EffectColor color)
    {
        var index = Shown.FindIndex(effect => effect.Icon == icon);

        if (color == EffectColor.None)
        {
            if (index >= 0)
                Shown.RemoveAt(index);
        } else if (index >= 0)
            Shown[index] = new HudEffect(icon, color);
        else
            Shown.Add(new HudEffect(icon, color));

        for (var i = 0; i < Slots.Length; i++)
            if (i < Shown.Count)
                Slots[i].Show(Shown[i].Icon, GoneOf(Shown[i].Color));
            else
                Slots[i].Clear();
    }

    /// <summary>
    ///     The part of the time that has gone for a colour band: none for white, five sixths for blue, all for no colour.
    /// </summary>
    internal static float GoneOf(EffectColor color)
    {
        var left = color switch
        {
            EffectColor.White  => 6,
            EffectColor.Red    => 5,
            EffectColor.Orange => 4,
            EffectColor.Yellow => 3,
            EffectColor.Green  => 2,
            EffectColor.Blue   => 1,
            _                  => 0
        };

        return 1f - (float)left / BANDS;
    }

    protected override void DrawChrome(SpriteBatch spriteBatch, Rectangle bounds) => TryDrawThemePart(spriteBatch, ThemeRoles.HudEffectBar, bounds);

    public override void Dispose()
    {
        Wedges.Dispose();

        base.Dispose();
    }
}
