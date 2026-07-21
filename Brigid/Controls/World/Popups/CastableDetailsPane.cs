#region
using Brigid.Collections;
using Brigid.Controls.Components;
using Brigid.Controls.Generic;
using Brigid.Controls.Scrolling;
using Brigid.Data.Models;
using Brigid.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Brigid.Controls.World.Popups;

/// <summary>
///     The <see cref="CastablePopupControl" /> Details tab: icon, name and level, the live cooldown, the cast-line
///     count, and the ability's description from the class's SClass metadata.
///     <para>
///         This is deliberately not the status-book detail popup. Right-click only reaches an ability the player has
///         already learned, so the requirement grid that popup exists to show — level/ability/master, the five stats,
///         prerequisites — is settled by definition and only crowds out what is still worth knowing mid-fight. The
///         requirement view still lives on the status book for abilities you don't have yet.
///     </para>
/// </summary>
internal sealed class CastableDetailsPane : UIPanel
{
    private const int ICON_SIZE = 32;
    private const int ROW_H = 14;
    private const int ROW_GAP = 2;
    private const int ICON_TO_TEXT = 8;
    private const int HEADER_ROWS = 3;

    private readonly UIImage Icon;
    private readonly UILabel Header;
    private readonly UILabel Cooldown;
    private readonly UILabel CastLines;
    private readonly UILabel DescriptionHeader;
    private readonly SelectableTextView Description;

    private byte BoundSlot;
    private bool BoundIsSpell;

    public CastableDetailsPane(Rectangle pane)
    {
        X = pane.X;
        Y = pane.Y;
        Width = pane.Width;
        Height = pane.Height;

        Icon = new UIImage
        {
            X = 0,
            Y = 0,
            Width = ICON_SIZE,
            Height = ICON_SIZE,
            IsHitTestVisible = false
        };
        AddChild(Icon);

        var textX = ICON_SIZE + ICON_TO_TEXT;
        var textW = pane.Width - textX;

        Header = AddRow(textX, 0, textW);
        Cooldown = AddRow(textX, ROW_H, textW);
        CastLines = AddRow(textX, 2 * ROW_H, textW);

        //clear both the icon and the header rows beside it, whichever is taller.
        var descriptionY = Math.Max(ICON_SIZE, HEADER_ROWS * ROW_H) + ROW_GAP * 2;

        DescriptionHeader = AddRow(0, descriptionY, pane.Width);
        DescriptionHeader.Text = "Description:";
        DescriptionHeader.ForegroundColor = DialogPalette.DisabledText;

        var bodyY = descriptionY + ROW_H;

        //the description takes every remaining pixel and scrolls: the prose is a sentence or two, but Hybrasyl
        //appends its own "Required Items:"/"Required Gold:" block, which runs the whole string to ten lines.
        Description = new SelectableTextView(new Rectangle(0, bodyY, pane.Width, pane.Height - bodyY));
        AddChild(Description);
    }

    private UILabel AddRow(int x, int y, int width)
    {
        var label = new UILabel
        {
            X = x,
            Y = y,
            Width = width,
            Height = ROW_H,
            PaddingLeft = 0,
            PaddingTop = 0,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            ForegroundColor = LegendColors.White,
            IsHitTestVisible = false
        };

        AddChild(label);

        return label;
    }

    /// <summary>
    ///     Populates the tab for a castable slot. Safe to call whether or not metadata exists for it —
    ///     <paramref name="lineCount" /> and the cooldown come from live slot state, so the tab is still useful for a
    ///     server-only castable the client's SClass table has never heard of.
    /// </summary>
    public void Bind(
        byte slot,
        string name,
        string level,
        Texture2D? icon,
        bool isSpell,
        int lineCount)
    {
        BoundSlot = slot;
        BoundIsSpell = isSpell;

        //the slot's own texture, not the metadata icon: for an already-learned ability the metadata icon's
        //learnable/locked duotone never applies, and this way the popup shows exactly the icon that was clicked.
        Icon.Texture = icon;
        Icon.TextureOffset = Vector2.Zero;

        Header.Text = string.IsNullOrEmpty(level) ? name : $"{name}  (Lev {level})";
        CastLines.Text = $"Castlines: {lineCount}";
        RefreshCooldown();

        AbilityMetadataEntry? entry = null;
        WorldState.AbilityMetadata?.TryGet(name, isSpell, out entry);

        if (string.IsNullOrWhiteSpace(entry?.Description))
            Description.SetText("No metadata available for this ability.", DialogPalette.DisabledText);
        else
            Description.SetText(entry.Description, TextColors.Default);
    }

    /// <summary>
    ///     The cooldown line is a ticking value, so it is polled rather than pushed — the books deliberately fire no
    ///     event for cooldown changes and every other consumer polls them the same way.
    /// </summary>
    public override void Update(GameTime gameTime)
    {
        base.Update(gameTime);

        if (Visible)
            RefreshCooldown();
    }

    private void RefreshCooldown()
    {
        var remainingMs = BoundIsSpell
            ? WorldState.SpellBook.GetCooldownRemainingMs(BoundSlot)
            : WorldState.SkillBook.GetCooldownRemainingMs(BoundSlot);

        if (remainingMs <= 0)
        {
            Cooldown.Text = "Cooldown: ready";
            Cooldown.ForegroundColor = LegendColors.White;

            return;
        }

        Cooldown.Text = $"Cooldown: {remainingMs / 1000f:0.0}s";
        Cooldown.ForegroundColor = LegendColors.Scarlet;
    }

    /// <summary>Drops the icon reference when the popup closes, mirroring the legacy popup's Hide.</summary>
    public void Release() => Icon.Texture = null;

    /// <summary>
    ///     Routes a key to the selectable description so Ctrl+C / select-all / caret navigation work. The popup is
    ///     the top control, so keyboard events stop there instead of descending — the same forwarding the board
    ///     read panels do for their body label.
    /// </summary>
    public void ForwardKey(KeyDownEvent e) => Description.ForwardKey(e);
}
