#region
using Brigid.Controls.Components;
using Chaos.DarkAges.Definitions;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Brigid.Controls.World.Hud;

/// <summary>
///     Vertical status effect bar on the right side of the HUD. Shows active persistent effects as half-size spell icons
///     with 2px-high horizontal color bars underneath each icon. Uses spelled.epf as the background strip (22x230). Only
///     visible when at least one effect is active. 10 slots, keyed by EffectIcon.
/// </summary>
public sealed class EffectBarControl : UIPanel
{
    private const int MAX_EFFECTS = 10;
    private const int SLOT_SIZE = 23;
    private const int SLOT_WIDTH = 22;

    private readonly EffectSlotControl[] Slots = new EffectSlotControl[MAX_EFFECTS];
    private int ActiveCount;

    public EffectBarControl()
    {
        Name = "EffectBar";
        Width = SLOT_WIDTH;
        Height = MAX_EFFECTS * SLOT_SIZE;
        Visible = false;
        IsHitTestVisible = false;

        //load background strip from spelled.epf (22x212)
        Background = UiRenderer.Instance!.GetEpfTexture("spelled.epf", 0);

        for (var i = 0; i < MAX_EFFECTS; i++)
        {
            Slots[i] = new EffectSlotControl
            {
                Name = $"Slot{i}",
                X = 0,
                Y = i * SLOT_SIZE,
                Width = SLOT_WIDTH,
                Height = SLOT_SIZE
            };

            AddChild(Slots[i]);
        }
    }

    public void ClearEffects()
    {
        for (var i = 0; i < MAX_EFFECTS; i++)
            Slots[i]
                .ClearEffect();

        ActiveCount = 0;
        UpdateVisibility();
    }

    private void RemoveEffect(byte effectIcon)
    {
        for (var i = 0; i < MAX_EFFECTS; i++)
            if (Slots[i].HasEffect && (Slots[i].EffectIcon == effectIcon))
            {
                Slots[i]
                    .ClearEffect();
                ActiveCount--;
                UpdateVisibility();

                return;
            }
    }

    private static Texture2D RenderHalfSizeIcon(byte iconId) => UiRenderer.Instance!.GetHalfSizeSpellIcon(iconId);

    /// <summary>
    ///     Updates or adds an effect. EffectIcon acts as a key — only one effect per icon value. EffectColor None removes the
    ///     effect.
    /// </summary>
    public void SetEffect(byte effectIcon, EffectColor effectColor)
    {
        if (effectColor == EffectColor.None)
        {
            RemoveEffect(effectIcon);

            return;
        }

        //check if this icon already exists — update color
        for (var i = 0; i < MAX_EFFECTS; i++)
            if (Slots[i].HasEffect && (Slots[i].EffectIcon == effectIcon))
            {
                Slots[i]
                    .UpdateColor(effectColor);

                return;
            }

        //find first empty slot
        for (var i = 0; i < MAX_EFFECTS; i++)
            if (!Slots[i].HasEffect)
            {
                Slots[i]
                    .SetEffect(effectIcon, effectColor, RenderHalfSizeIcon(effectIcon));
                ActiveCount++;
                UpdateVisibility();

                return;
            }
    }

    private void UpdateVisibility() => Visible = ActiveCount > 0;
}