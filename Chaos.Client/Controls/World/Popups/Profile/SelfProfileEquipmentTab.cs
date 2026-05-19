#region
using Chaos.Client.Controls.Components;
using Chaos.Client.Data;
using Chaos.DarkAges.Definitions;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Chaos.Client.Controls.World.Popups.Profile;

/// <summary>
///     Equipment tab page within the status book, loaded from _nui_eq prefab. Displays 18 equipment slots as a paper doll
///     layout with item icons. Each slot has a fixed position from the prefab and maps to an <see cref="EquipmentSlot" />.
///     Empty slots show a placeholder icon from _nui_eqi; occupied slots show the item's panel icon.
/// </summary>
public sealed class SelfProfileEquipmentTab : PrefabPanel
{
    //emoticon status icon frame index → _nemots.spf frame
    private const int EMOTICON_FRAME_COUNT = 8;

    //idle frame for south-facing direction (walk anim frames 5-9, idle = 5)
    private const int PAPERDOLL_IDLE_FRAME = 5;
    private readonly UILabel? AcLabel;
    private readonly UILabel? ClanLabel;
    private readonly UILabel? ClanTitleLabel;
    private readonly UILabel? ClassLabel;
    private readonly UILabel? ConLabel;

    private readonly UILabel? DexLabel;

    //emoticon status
    private readonly Texture2D?[] EmoticonIcons;

    private readonly UILabel? EmoticonLabel;
    private readonly UIButton? GroupBtn;
    private readonly Texture2D? GroupClosedTexture;
    private readonly Texture2D? GroupOpenTexture;
    private readonly UIImage? EmoticonImage;
    private readonly UILabel? IntLabel;

    //player info labels
    private readonly UILabel? NameLabel;

    //nation icon and text
    private readonly UIImage? NationImage;
    private readonly UILabel? NationTextLabel;

    //paperdoll
    private readonly UIImage? PaperdollImage;

    //portrait and profile text
    private readonly UILabel? PortraitTextLabel;

    //equipment slot rendering: maps equipmentslot to its visual state
    private readonly Dictionary<EquipmentSlot, EquipmentSlotVisual> SlotVisuals = [];

    //stat labels from the _nui_eq prefab (n_ prefix)
    private readonly UILabel? StrLabel;
    private readonly UILabel? TitleLabel;

    //tooltip for hovered equipment slot
    private readonly UILabel TooltipLabel;
    private readonly UILabel? WisLabel;
    private Texture2D? NationIconTexture;
    private Texture2D? PaperdollTexture;

    /// <summary>
    ///     Gets the current profile text from the label.
    /// </summary>
    public string ProfileText => PortraitTextLabel?.Text ?? string.Empty;

    public SelfProfileEquipmentTab(string prefabName)
        : base(prefabName, false)
    {
        Name = prefabName;
        Visible = false;

        //build slot visuals from prefab-created image elements.
        //createimage creates uiimage elements for controls that have images.
        //each slot image initially shows its _nui_eqi placeholder icon.
        foreach ((var controlName, var slot) in Constants.EquipmentSlotsByControlName)
        {
            if (CreateImage(controlName) is not { } slotImage)
                continue;

            //the placeholder texture was already set from the _nui_eqi frame
            var visual = new EquipmentSlotVisual
            {
                Image = slotImage,
                PlaceholderTexture = slotImage.Texture
            };

            SlotVisuals[slot] = visual;
        }

        //stat labels — right-aligned numeric values
        StrLabel = CreateLabel("N_STR", HorizontalAlignment.Right);
        StrLabel?.TruncateWithEllipsis = false;
        
        IntLabel = CreateLabel("N_INT", HorizontalAlignment.Right);
        IntLabel?.TruncateWithEllipsis = false;
        
        WisLabel = CreateLabel("N_WIS", HorizontalAlignment.Right);
        WisLabel?.TruncateWithEllipsis = false;
        
        ConLabel = CreateLabel("N_CON", HorizontalAlignment.Right);
        ConLabel?.TruncateWithEllipsis = false;
        
        DexLabel = CreateLabel("N_DEX", HorizontalAlignment.Right);
        DexLabel?.TruncateWithEllipsis = false;
        
        AcLabel = CreateLabel("N_AC", HorizontalAlignment.Right);
        AcLabel?.TruncateWithEllipsis = false;

        //player info labels — left-aligned text
        NameLabel = CreateLabel("NAME");
        ClassLabel = CreateLabel("CLASSTEXT");
        ClanLabel = CreateLabel("CLANTEXT");
        ClanLabel?.TruncateWithEllipsis = false;
        
        ClanTitleLabel = CreateLabel("CLANTITLETEXT");
        ClanTitleLabel?.TruncateWithEllipsis = false;
        
        TitleLabel = CreateLabel("TITLETEXT");
        TitleLabel?.TruncateWithEllipsis = false;

        //group button — single button that swaps textures based on groupopen state.
        //groupbtn prefab has the "open/recruiting" images, groupbtn_disabled has the "closed" images.
        GroupBtn = CreateButton("GroupBtn");

        if (GroupBtn is not null)
        {
            GroupOpenTexture = GroupBtn.NormalTexture;
            GroupBtn.PressedTexture = null;
            GroupBtn.Clicked += () => OnGroupToggled?.Invoke();
        }

        //extract the closed-state texture from groupbtn_disabled for the closed state icon
        if (CreateImage("GroupBtn_Disabled") is { } disabledImage)
        {
            GroupClosedTexture = disabledImage.Texture;
            Children.Remove(disabledImage);
            disabledImage.Dispose();
        }

        //nation icon and text
        NationImage = CreateImage("Nation");

        if (NationImage is not null)
            NationImage.ScaleToFit = true;

        NationTextLabel = CreateLabel("NationText");
        NationTextLabel?.VerticalAlignment = VerticalAlignment.Top;
        NationTextLabel?.ForegroundColor = LegendColors.White;

        //paperdoll area
        PaperdollImage = CreateImage("HumanImage");

        //portrait and profile text
        CreateImage("Portrait");
        PortraitTextLabel = CreateLabel("PortraitText");

        if (PortraitTextLabel is not null)
        {
            PortraitTextLabel.WordWrap = true;
            PortraitTextLabel.ForegroundColor = Color.White;
        }

        //emoticon status areas
        var humanIconRect = GetRect("HumanIcon");

        //load emoticon icons from _nemots.spf (frames 0-7)
        EmoticonIcons = new Texture2D?[EMOTICON_FRAME_COUNT];

        for (var i = 0; i < EMOTICON_FRAME_COUNT; i++)
            EmoticonIcons[i] = UiRenderer.Instance!.GetSpfTexture("_nemots.spf", i);

        //emoticon status text label — prefab places it at the same origin as the icon, so shift
        //it right past the icon to avoid overlap
        EmoticonLabel = CreateLabel("HumanState");
        EmoticonLabel?.ForegroundColor = LegendColors.White;

        if ((EmoticonLabel is not null) && (humanIconRect != Rectangle.Empty))
            EmoticonLabel.X += humanIconRect.Width + 2;

        //emoticon icon — drawn as a uiimage child so it participates in the regular child render
        //pipeline. this ensures zindex ordering works correctly, allowing the tooltip (zindex 10)
        //to draw on top of the emoticon icon.
        if (humanIconRect != Rectangle.Empty)
        {
            EmoticonImage = new UIImage
            {
                Name = "EmoticonIcon",
                X = humanIconRect.X,
                Y = humanIconRect.Y,
                Width = humanIconRect.Width,
                Height = humanIconRect.Height,
                Texture = EmoticonIcons[0]
            };
            AddChild(EmoticonImage);
        }

        //tooltip label — hidden by default, follows cursor when an equipment slot is hovered
        TooltipLabel = new UILabel
        {
            Name = "Tooltip",
            Visible = false,
            IsHitTestVisible = false,
            PaddingLeft = 1,
            PaddingTop = 1,
            BackgroundColor = new Color(
                0,
                0,
                0,
                128),
            BorderColor = LegendColors.White,
            ForegroundColor = LegendColors.White,
            ZIndex = 10
        };

        AddChild(TooltipLabel);
    }

    /// <summary>
    ///     Clears all equipment slot icons, restoring placeholders.
    /// </summary>
    public void ClearAllSlots()
    {
        foreach ((_, var visual) in SlotVisuals)
        {
            if (visual.ItemTexture is not null)
            {
                visual.ItemTexture.Dispose();
                visual.ItemTexture = null;
            }

            visual.Image.Texture = visual.PlaceholderTexture;
        }
    }

    /// <summary>
    ///     Clears the item icon for a specific equipment slot, restoring the placeholder.
    /// </summary>
    public void ClearSlot(EquipmentSlot slot)
    {
        if (!SlotVisuals.TryGetValue(slot, out var visual))
            return;

        if (visual.ItemTexture is not null)
        {
            visual.ItemTexture.Dispose();
            visual.ItemTexture = null;
        }

        visual.Image.Texture = visual.PlaceholderTexture;
    }

    /// <summary>
    ///     Returns true if the given screen point is within any equipment slot image.
    /// </summary>
    public bool ContainsEquipmentSlotPoint(int screenX, int screenY)
    {
        foreach ((_, var visual) in SlotVisuals)
            if (visual.Image.ContainsPoint(screenX, screenY))
                return true;

        return false;
    }

    public override void Dispose()
    {
        foreach ((_, var visual) in SlotVisuals)
            if (visual.ItemTexture is not null)
            {
                if (visual.Image.Texture == visual.ItemTexture)
                    visual.Image.Texture = null;

                visual.ItemTexture.Dispose();
            }

        SlotVisuals.Clear();
        NationIconTexture?.Dispose();
        PaperdollTexture?.Dispose();

        //clear the emoticon image texture so uiimage.dispose doesn't dispose the cached spf texture
        EmoticonImage?.Texture = null;

        //uiimage children are disposed by base.dispose, but we own the dynamic textures
        base.Dispose();
    }

    public event GroupToggledHandler? OnGroupToggled;
    public event ProfileTextClickedHandler? OnProfileTextClicked;
    public event UnequipHandler? OnUnequip;

    /// <summary>
    ///     Renders an item icon from the panel item sprite sheet using the same pipeline as inventory icons.
    /// </summary>
    /// <summary>
    ///     Sets the emoticon/social status icon and text. State 0-7 maps to _nemots.spf frames.
    /// </summary>
    public void SetEmoticonState(byte state, string statusText)
    {
        EmoticonLabel?.Text = statusText;

        if ((EmoticonImage is not null) && (state < EmoticonIcons.Length))
            EmoticonImage.Texture = EmoticonIcons[state];
    }

    /// <summary>
    ///     Swaps the group button texture between recruiting (open) and closed states.
    /// </summary>
    public void SetGroupOpen(bool groupOpen)
    {
        GroupBtn?.NormalTexture = groupOpen ? GroupOpenTexture : GroupClosedTexture;
    }

    /// <summary>
    ///     Sets the nation icon (modern-first via .datf nation-badge pack, legacy fallback to _nui_nat.spf frame
    ///     <c>nationId - 1</c>). Modern badges are centered within the slot; a negative offset overflows the slot
    ///     bounds when the texture is larger (e.g. 64×64 badge in a ~54×64 slot).
    /// </summary>
    public void SetNation(byte nationId)
    {
        NationIconTexture?.Dispose();
        NationIconTexture = null;

        if (nationId > 0)
            NationIconTexture = UiRenderer.Instance!.GetNationBadge(nationId);

        if (NationImage is not null)
            NationImage.Texture = NationIconTexture;

        if (NationTextLabel is not null)
        {
            var nationMeta = DataContext.MetaFiles.GetNationMetadata();
            NationTextLabel.Text = nationMeta?.Nations.TryGetValue(nationId, out var name) == true ? name : string.Empty;
        }
    }

    /// <summary>
    ///     Renders the paperdoll using the player's current appearance. Uses the full AislingRenderer at the south-facing idle
    ///     frame (same composition as the world aisling, just frozen).
    /// </summary>
    public void SetPaperdoll(AislingRenderer renderer, in AislingAppearance appearance)
    {
        PaperdollTexture?.Dispose();

        //south-facing (direction=2) = right idle frame (5) + horizontal flip
        PaperdollTexture = renderer.Render(in appearance, PAPERDOLL_IDLE_FRAME, flipHorizontal: true);

        PaperdollImage?.Texture = PaperdollTexture;
    }

    /// <summary>
    ///     Updates the player identity labels (name, class, clan, title).
    /// </summary>
    public void SetPlayerInfo(
        string name,
        string className,
        string clanName,
        string clanTitle,
        string title)
    {
        NameLabel?.ForegroundColor = LegendColors.White;
        NameLabel?.Text = name;
        ClassLabel?.ForegroundColor = LegendColors.White;
        ClassLabel?.Text = className;
        ClanLabel?.ForegroundColor = LegendColors.White;
        ClanLabel?.Text = clanName;
        ClanTitleLabel?.ForegroundColor = LegendColors.White;
        ClanTitleLabel?.Text = clanTitle;
        TitleLabel?.ForegroundColor = LegendColors.White;
        TitleLabel?.Text = title;
    }

    /// <summary>
    ///     Sets the profile text on the display label.
    /// </summary>
    public void SetProfileText(string text)
    {
        PortraitTextLabel?.Text = text;
    }

    /// <summary>
    ///     Sets the item icon for a specific equipment slot.
    /// </summary>
    public void SetSlot(EquipmentSlot slot, ushort sprite, DisplayColor color, string? itemName = null)
    {
        if (!SlotVisuals.TryGetValue(slot, out var visual))
            return;

        //dispose previous item texture (not the placeholder — that's shared/owned by the prefab)
        if (visual.ItemTexture is not null)
        {
            visual.ItemTexture.Dispose();
            visual.ItemTexture = null;
        }

        visual.ItemName = itemName ?? string.Empty;

        var texture = UiRenderer.Instance!.GetItemIcon(sprite, color);
        visual.ItemTexture = texture;
        visual.Image.Texture = texture;
    }

    /// <summary>
    ///     Updates the stat display labels on the equipment page.
    /// </summary>
    public void UpdateStats(
        int str,
        int intel,
        int wis,
        int con,
        int dex,
        int ac)
    {
        StrLabel?.Text = $"{str}";
        IntLabel?.Text = $"{intel}";
        WisLabel?.Text = $"{wis}";
        ConLabel?.Text = $"{con}";
        DexLabel?.Text = $"{dex}";
        AcLabel?.Text = $"{ac}";
    }

    public override void OnMouseMove(MouseMoveEvent e)
    {
        EquipmentSlot? foundSlot = null;
        string? foundName = null;

        foreach ((var slot, var visual) in SlotVisuals)
            if (visual.Image.ContainsPoint(e.ScreenX, e.ScreenY) && (visual.ItemTexture is not null))
            {
                foundSlot = slot;
                foundName = visual.ItemName;

                break;
            }

        if (foundSlot is not null && !string.IsNullOrEmpty(foundName))
        {
            TooltipLabel.Text = foundName;
            TooltipLabel.Width = TextRenderer.MeasureWidth(foundName) + 4;
            TooltipLabel.Height = TextRenderer.CHAR_HEIGHT + 4;
            TooltipLabel.X = e.ScreenX - ScreenX + 12;
            TooltipLabel.Y = e.ScreenY - ScreenY + 12;
            TooltipLabel.Visible = true;
        } else
            TooltipLabel.Visible = false;
    }

    public override void OnMouseLeave()
    {
        TooltipLabel.Visible = false;
    }

    public override void OnClick(ClickEvent e)
    {
        if (e.Button != MouseButton.Left)
            return;

        foreach ((var slot, var visual) in SlotVisuals)
            if (visual.Image.ContainsPoint(e.ScreenX, e.ScreenY) && (visual.ItemTexture is not null))
            {
                OnUnequip?.Invoke(slot);
                e.Handled = true;

                return;
            }

        //check if portrait text area was clicked
        if (PortraitTextLabel is not null && PortraitTextLabel.ContainsPoint(e.ScreenX, e.ScreenY))
        {
            OnProfileTextClicked?.Invoke();
            e.Handled = true;
        }
    }

    private sealed class EquipmentSlotVisual
    {
        public required UIImage Image { get; init; }
        public string ItemName { get; set; } = string.Empty;
        public Texture2D? ItemTexture { get; set; }
        public Texture2D? PlaceholderTexture { get; init; }
    }
}