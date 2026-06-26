#region
using Brigid.Controls.Components;
using Brigid.Utilities;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using SkiaSharp;
#endregion

namespace Brigid.Controls.World.Popups;

/// <summary>
///     Inventory item tooltip with black background, dlgframe.epf border, white item name (word-wrapped at 25 chars),
///     and blue durability text. Follows the cursor when visible.
/// </summary>
public sealed class ItemTooltipControl : UIPanel
{
    private const int MAX_CONTENT_WIDTH = 25 * TextRenderer.CHAR_WIDTH;
    private const int PADDING = 6;

    private static readonly Color DurabilityColor = new(100, 149, 237);

    private readonly UILabel DurabilityLabel;
    private readonly UILabel NameLabel;
    private readonly UIImage TooltipImage;

    public ItemTooltipControl()
    {
        Name = "ItemTooltip";
        Visible = false;
        IsHitTestVisible = false;

        TooltipImage = new UIImage
        {
            Name = "TooltipContent"
        };

        NameLabel = new UILabel
        {
            X = PADDING,
            Y = PADDING,
            Width = MAX_CONTENT_WIDTH,
            Height = TextRenderer.CHAR_HEIGHT,
            WordWrap = true,
            PaddingLeft = 0,
            PaddingTop = 0,
            ForegroundColor = LegendColors.White
        };

        DurabilityLabel = new UILabel
        {
            X = PADDING,
            Width = MAX_CONTENT_WIDTH,
            Height = TextRenderer.CHAR_HEIGHT,
            WordWrap = true,
            PaddingLeft = 0,
            PaddingTop = 0,
            ForegroundColor = DurabilityColor
        };

        AddChild(TooltipImage);
        AddChild(NameLabel);
        AddChild(DurabilityLabel);
    }

    private static Texture2D CompositeBackground(int totalWidth, int totalHeight)
    {
        using var background = DialogFrame.Composite(
            new SKColor(
                0,
                0,
                0,
                255),
            totalWidth,
            totalHeight);

        var info = new SKImageInfo(
            totalWidth,
            totalHeight,
            SKColorType.Rgba8888,
            SKAlphaType.Premul);
        using var surface = SKSurface.Create(info);
        var canvas = surface.Canvas;

        if (background is not null)
            canvas.DrawImage(background, 0, 0);
        else
            canvas.Clear(
                new SKColor(
                    0,
                    0,
                    0,
                    255));

        using var snapshot = surface.Snapshot();

        return TextureConverter.ToTexture2D(snapshot);
    }

    public void Hide() => Visible = false;

    /// <summary>
    ///     Positions the tooltip relative to the cursor. Flips to the left of the cursor when the default right-side
    ///     placement would overflow the virtual viewport. Must be called after <see cref="UIElement.Width"/> and
    ///     <see cref="UIElement.Height"/> are set.
    /// </summary>
    public void UpdatePosition(int mouseX, int mouseY)
    {
        var rightX = mouseX + 15;

        X = (rightX + Width) <= ChaosGame.VIRTUAL_WIDTH ? rightX : mouseX - Width;
        Y = Math.Clamp(mouseY + 15, 0, ChaosGame.VIRTUAL_HEIGHT - Height);
    }

    public void Show(
        string itemName,
        int currentDurability,
        int maxDurability,
        int mouseX,
        int mouseY)
    {
        NameLabel.Text = itemName;
        var nameHeight = NameLabel.ContentHeight;
        NameLabel.Height = nameHeight;

        var hasDurability = maxDurability > 0;
        DurabilityLabel.Text = hasDurability ? $"{currentDurability}/{maxDurability}" : string.Empty;
        DurabilityLabel.Y = PADDING + nameHeight;
        DurabilityLabel.Height = DurabilityLabel.ContentHeight;
        DurabilityLabel.Visible = hasDurability;

        var contentHeight = nameHeight;

        if (hasDurability)
            contentHeight += DurabilityLabel.ContentHeight;

        var totalWidth = PADDING + MAX_CONTENT_WIDTH + PADDING;
        var totalHeight = PADDING + contentHeight + PADDING;

        Width = totalWidth;
        Height = totalHeight;

        TooltipImage.Texture?.Dispose();

        var texture = CompositeBackground(totalWidth, totalHeight);
        TooltipImage.Texture = texture;
        TooltipImage.X = 0;
        TooltipImage.Y = 0;
        TooltipImage.Width = totalWidth;
        TooltipImage.Height = totalHeight;

        UpdatePosition(mouseX, mouseY);

        Visible = true;
    }
}