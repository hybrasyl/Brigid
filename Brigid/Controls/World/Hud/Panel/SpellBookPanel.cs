#region
using Brigid.Collections;
using Brigid.Controls.World.Hud.Panel.Slots;
using Brigid.Data.Models;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Brigid.Controls.World.Hud.Panel;

/// <summary>
///     Spell book panel (D key, Shift+D for secondary). Thin view that subscribes to
///     <see cref="ViewModel.SpellBook" /> change events and renders spell icons with Swap-style cooldowns.
/// </summary>
public sealed class SpellBookPanel : PanelBase
{
    private const int MAX_SLOTS = 90;

    public SpellBookPanel(
        ControlPrefabSet hudPrefabSet,
        SkillBookPage page = SkillBookPage.Page1,
        Texture2D? background = null,
        int normalVisibleSlots = DEFAULT_VISIBLE_SLOTS,
        int columns = DEFAULT_COLUMNS,
        int? cellCount = null,
        int gridOffsetX = 8,
        bool drawSlotNumberOverlay = true,
        bool loadFallbackBackground = true,
        int? compactGridPadding = null)
        : base(
            hudPrefabSet,
            MAX_SLOTS,
            CooldownStyle.Swap,
            (int)page,
            columns,
            cellCount,
            gridOffsetX,
            background: background,
            normalVisibleSlots: normalVisibleSlots,
            drawSlotNumberOverlay: drawSlotNumberOverlay,
            loadFallbackBackground: loadFallbackBackground,
            compactGridPadding: compactGridPadding)
    {
        Name = page switch
        {
            SkillBookPage.Page1 => "SpellBook",
            SkillBookPage.Page2 => "SpellBookAlt",
            SkillBookPage.Page3 => "SpellBookWorld",
            _                   => "SpellBook"
        };

        WorldState.SpellBook.SlotChanged += OnSlotChanged;
        WorldState.SpellBook.Cleared += OnCleared;
    }

    protected override PanelSlot CreateSlot(byte slotNumber, string name, CooldownStyle cooldownStyle)
        => new SpellSlot
        {
            Name = name,
            Slot = slotNumber,
            CooldownStyle = cooldownStyle
        };

    public override void Dispose()
    {
        WorldState.SpellBook.SlotChanged -= OnSlotChanged;
        WorldState.SpellBook.Cleared -= OnCleared;

        foreach (var slot in Slots)
        {
            slot.CooldownTexture?.Dispose();
            slot.CooldownTexture = null;
        }

        base.Dispose();
    }

    /// <summary>
    ///     Returns the SpellSlot for a 1-based slot number, or null.
    /// </summary>
    public SpellSlot? GetSpellSlot(byte slot) => FindSlot(slot) as SpellSlot;

    private void OnCleared()
    {
        foreach (var slot in Slots)
        {
            slot.NormalTexture?.Dispose();
            slot.NormalTexture = null;
            slot.CooldownTexture?.Dispose();
            slot.CooldownTexture = null;
            slot.CooldownPercent = 0;
            slot.SlotName = null;
        }
    }

    private void OnSlotChanged(byte slot)
    {
        var control = FindSlot(slot);

        if (control is null)
            return;

        var data = WorldState.SpellBook.GetSlot(slot);

        //dispose old textures — sprite may have changed
        control.CooldownTexture?.Dispose();
        control.CooldownTexture = null;

        if (data.IsOccupied)
        {
            control.NormalTexture?.Dispose();
            var icon = RenderIcon(data.Sprite);
            control.NormalTexture = icon.Texture;
            control.TextureOffset = new Vector2(icon.OffsetX, icon.OffsetY);

            if (control is SpellSlot spellSlot)
            {
                spellSlot.SpellType = data.SpellType;
                spellSlot.Prompt = data.Prompt ?? string.Empty;
                spellSlot.CastLines = data.CastLines;

                if (data.Chants is not null)
                    Array.Copy(data.Chants, spellSlot.Chants, Math.Min(data.Chants.Length, 10));
            }

            SetSlotName(slot, data.Name);
        } else
        {
            control.NormalTexture?.Dispose();
            control.NormalTexture = null;
            control.SlotName = null;
            control.CooldownPercent = 0;
            control.CurrentDurability = 0;
            control.MaxDurability = 0;
        }
    }

    protected override IconTexture RenderIcon(ushort spriteId) => UiRenderer.Instance!.GetSpellIcon(spriteId);

    private Texture2D RenderTintedIcon(ushort spriteId)
    {
        var cache = UiRenderer.Instance!;

        return cache.GetCooldownTintedTexture($"spell:{spriteId}", cache.GetSpellIcon(spriteId).Texture, LegendColors.CornflowerBlue);
    }

    public override void Update(GameTime gameTime)
    {
        base.Update(gameTime);

        //read cooldown state each frame — swap style: fully on or off
        for (var i = 0; (i < VisibleSlotCount) && (i < Slots.Count); i++)
        {
            var slot = (byte)(i + SlotOffset + 1);
            var control = Slots[i];
            var isOnCooldown = WorldState.SpellBook.IsOnCooldown(slot);

            if (isOnCooldown && control.CooldownTexture is null && control.NormalTexture is not null)
            {
                var data = WorldState.SpellBook.GetSlot(slot);

                if (data.IsOccupied)
                    control.CooldownTexture = RenderTintedIcon(data.Sprite);
            }

            control.CooldownPercent = isOnCooldown ? 1f : 0;
        }
    }
}