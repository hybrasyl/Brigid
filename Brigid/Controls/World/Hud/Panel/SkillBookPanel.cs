#region
using Brigid.Collections;
using Brigid.Controls.World.Hud.Panel.Slots;
using Brigid.Data.Models;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Brigid.Controls.World.Hud.Panel;

/// <summary>
///     Skill book panel (S key, Shift+S for secondary). Thin view that subscribes to
///     <see cref="ViewModel.SkillBook" /> change events and renders skill icons with Progressive-style cooldowns.
/// </summary>
public sealed class SkillBookPanel : PanelBase
{
    private const int MAX_SLOTS = 89;

    public SkillBookPanel(
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
            CooldownStyle.Progressive,
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
            SkillBookPage.Page1 => "SkillBook",
            SkillBookPage.Page2 => "SkillBookAlt",
            SkillBookPage.Page3 => "SkillBookWorld",
            _                   => "SkillBook"
        };

        WorldState.SkillBook.SlotChanged += OnSlotChanged;
        WorldState.SkillBook.Cleared += OnCleared;
    }

    protected override PanelSlot CreateSlot(byte slotNumber, string name, CooldownStyle cooldownStyle)
        => new SkillSlot
        {
            Name = name,
            Slot = slotNumber,
            CooldownStyle = cooldownStyle
        };

    public override void Dispose()
    {
        WorldState.SkillBook.SlotChanged -= OnSlotChanged;
        WorldState.SkillBook.Cleared -= OnCleared;

        foreach (var slot in Slots)
        {
            slot.CooldownTexture?.Dispose();
            slot.CooldownTexture = null;
            slot.GreyTexture?.Dispose();
            slot.GreyTexture = null;
        }

        base.Dispose();
    }

    /// <summary>
    ///     Returns the SkillSlot for a 1-based slot number, or null.
    /// </summary>
    public SkillSlot? GetSkillSlot(byte slot) => FindSlot(slot) as SkillSlot;

    private void OnCleared()
    {
        foreach (var slot in Slots)
        {
            slot.NormalTexture?.Dispose();
            slot.NormalTexture = null;
            slot.CooldownTexture?.Dispose();
            slot.CooldownTexture = null;
            slot.GreyTexture?.Dispose();
            slot.GreyTexture = null;
            slot.CooldownPercent = 0;
            slot.SlotName = null;
        }
    }

    private void OnSlotChanged(byte slot)
    {
        var control = FindSlot(slot);

        if (control is null)
            return;

        var data = WorldState.SkillBook.GetSlot(slot);

        //dispose old cooldown textures — sprite may have changed
        control.CooldownTexture?.Dispose();
        control.CooldownTexture = null;
        control.GreyTexture?.Dispose();
        control.GreyTexture = null;

        if (data.IsOccupied)
        {
            control.NormalTexture?.Dispose();
            var icon = RenderIcon(data.Sprite);
            control.NormalTexture = icon.Texture;
            control.TextureOffset = new Vector2(icon.OffsetX, icon.OffsetY);

            if (control is SkillSlot skillSlot)
                skillSlot.Chant = data.Chant ?? string.Empty;

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

    private Texture2D RenderGreyIcon(ushort spriteId)
    {
        var cache = UiRenderer.Instance!;

        return cache.GetCooldownTintedTexture($"skill:{spriteId}", cache.GetSkillIcon(spriteId).Texture, LegendColors.DimGray);
    }

    protected override IconTexture RenderIcon(ushort spriteId) => UiRenderer.Instance!.GetSkillIcon(spriteId);

    private Texture2D RenderTintedIcon(ushort spriteId)
    {
        var cache = UiRenderer.Instance!;

        return cache.GetCooldownTintedTexture($"skill:{spriteId}", cache.GetSkillIcon(spriteId).Texture, LegendColors.CornflowerBlue);
    }

    public override void Update(GameTime gameTime)
    {
        base.Update(gameTime);

        //read cooldown state each frame — progressive style: grey base with blue overlay
        for (var i = 0; (i < VisibleSlotCount) && (i < Slots.Count); i++)
        {
            var slot = (byte)(i + SlotOffset + 1);
            var control = Slots[i];
            var cooldownPercent = WorldState.SkillBook.GetCooldownPercent(slot);

            if ((cooldownPercent > 0) && control.NormalTexture is not null)
            {
                var data = WorldState.SkillBook.GetSlot(slot);

                if (data.IsOccupied)
                {
                    control.GreyTexture ??= RenderGreyIcon(data.Sprite);
                    control.CooldownTexture ??= RenderTintedIcon(data.Sprite);
                }
            }

            control.CooldownPercent = cooldownPercent;
        }
    }
}