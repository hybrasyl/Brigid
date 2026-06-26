#region
using Brigid.Controls.Components;
using Chaos.DarkAges.Definitions;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Brigid.Controls.World.Hud;

/// <summary>
///     A single slot in the effect bar: half-size spell icon + 2px-high colored duration bar.
/// </summary>
public sealed class EffectSlotControl : UIPanel
{
    private const int ICON_SIZE = 15;
    private const int ICON_OFFSET_X = 3;
    private const int ICON_OFFSET_Y = 3;
    private const int BAR_HEIGHT = 2;
    private const int BAR_MAX_WIDTH = 14;
    private const int BAR_OFFSET_X = 4;

    private readonly UIProgressBar Bar;
    private readonly UIImage Icon;

    public byte EffectIcon { get; private set; }

    /// <summary>
    ///     True if this slot currently holds an effect. Derived from <see cref="UIProgressBar.FillColor" />, which
    ///     <see cref="ApplyBarColor" /> sets to non-null for any active effect and <see cref="ClearEffect" /> resets to
    ///     null — so slot matching doesn't rely on <c>EffectIcon == 0</c> as a sentinel (icon 0 is a valid icon).
    /// </summary>
    public bool HasEffect => Bar.FillColor is not null;

    public EffectSlotControl()
    {
        Visible = false;

        Icon = new UIImage
        {
            Name = "Icon",
            X = ICON_OFFSET_X,
            Y = ICON_OFFSET_Y,
            Width = ICON_SIZE,
            Height = ICON_SIZE
        };

        AddChild(Icon);

        Bar = new UIProgressBar
        {
            Name = "Bar",
            X = BAR_OFFSET_X,
            Y = ICON_OFFSET_Y + ICON_SIZE + 1,
            Width = BAR_MAX_WIDTH,
            Height = BAR_HEIGHT
        };

        AddChild(Bar);
    }

    private void ApplyBarColor(EffectColor color)
    {
        Bar.Percent = GetBarPercent(color);
        Bar.FillColor = Bar.Percent > 0 ? GetBarColor(color) : null;
    }

    public void ClearEffect()
    {
        Icon.Texture?.Dispose();
        Icon.Texture = null;
        Bar.Percent = 0;
        Bar.FillColor = null;
        EffectIcon = 0;
        Visible = false;
    }

    private static Color GetBarColor(EffectColor effectColor)
        => effectColor switch
        {
            EffectColor.Blue   => new Color(59, 82, 120),
            EffectColor.Green  => new Color(0, 180, 0),
            EffectColor.Yellow => Color.Yellow,
            EffectColor.Orange => new Color(255, 165, 0),
            EffectColor.Red    => Color.Red,
            EffectColor.White  => Color.White,
            _                  => Color.Transparent
        };

    private static float GetBarPercent(EffectColor effectColor)
        => effectColor switch
        {
            EffectColor.White  => 1f,
            EffectColor.Red    => 6f / 7f,
            EffectColor.Orange => 5f / 7f,
            EffectColor.Yellow => 4f / 7f,
            EffectColor.Green  => 3f / 7f,
            EffectColor.Blue   => 2f / 7f,
            _                  => 0f
        };

    public void SetEffect(byte iconId, EffectColor color, Texture2D? iconTexture)
    {
        EffectIcon = iconId;
        Icon.Texture = iconTexture;
        ApplyBarColor(color);
        Visible = true;
    }

    public void UpdateColor(EffectColor color) => ApplyBarColor(color);
}