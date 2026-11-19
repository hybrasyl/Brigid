#region
using System.Globalization;
using Brigid.Controls.Components;
using Brigid.Controls.Generic;
using Brigid.Controls.World.Hud.Main.Windows;
using Brigid.Definitions;
using Brigid.Models;
using Brigid.Rendering;
using Brigid.Systems.Keybinds;
using Brigid.ViewModel;
using Chaos.DarkAges.Definitions;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Brigid.Controls.World.Hud.Main;

/// <summary>
///     The hotbar of the new HUD (BRIG-146, D4, D27, D28), in <see cref="MainHudLayout.Hotbar" />: the page column (an
///     arrow up, the page number, an arrow down) and twelve <see cref="HotbarSlotControl" />s with the keys 1 to 0, - and =.
///     It shows the page of <see cref="ViewModel.Hotbar" /> that shows, and reads the state of each entry at each update.
/// </summary>
/// <remarks>
///     A drop of an ability from the Abilities window or of an item from the Inventory window sets the entry of a slot,
///     in place of the entry there. A drag of a slot to another slot swaps the two entries; a drop of a slot on the hotbar
///     but not on a slot keeps it; a drag of a slot that ends anywhere else clears it, and does nothing more. A left click
///     uses the entry, as its number key does, and a right click on an ability asks for its castable popup. The arrows and
///     the wheel change the page; the page wraps. The tooltip shows the name and the key.
/// </remarks>
internal sealed class HotbarControl : UIPanel
{
    /// <summary>The labels of the keys of the slots, from the left: the names of <see cref="Keybinds.NumberRowKeys" />.</summary>
    public static readonly IReadOnlyList<string> KeyLabels = Keybinds.NumberRowKeys.Select(KeybindCatalog.KeyName).ToArray();

    private readonly Hotbar Model;
    private readonly HudTooltip Tooltip;
    private readonly Func<HotbarEntry, HotbarEntryState, IconTexture?> IconSource;
    private readonly Func<object?> ActiveDrag;
    private readonly ClockWedgeCache Wedges = new(MainHudLayout.HOTBAR_SLOT);
    private readonly HotbarSlotControl[] Slots = new HotbarSlotControl[Hotbar.SLOTS];
    private readonly UILabel PageText;

    //the drag of a slot in progress, and whether a modal cancelled its drop; the hotbar clears the slot when the drag ends
    //with no drop on the hotbar
    private HotbarDragPayload? Outgoing;
    private bool OutgoingCancelled;

    //the slot under the pointer, and whether its tooltip shows; the entry under the pointer can change with no enter
    //(the wheel changes the page, a drop fills the slot, an entry empties)
    private int? Hovered;
    private bool TooltipShown;
    private int ShownPage = -1;

    /// <param name="model">The hotbar.</param>
    /// <param name="tooltip">The tooltip of the HUD.</param>
    /// <param name="iconSource">The icon of an entry in its state; null for the icons of <see cref="UiRenderer" />.</param>
    /// <param name="activeDrag">The payload of the drag in progress; null for the drag of <see cref="InputDispatcher" />.</param>
    public HotbarControl(
        Hotbar model,
        HudTooltip tooltip,
        Func<HotbarEntry, HotbarEntryState, IconTexture?>? iconSource = null,
        Func<object?>? activeDrag = null)
    {
        Model = model;
        Tooltip = tooltip;
        IconSource = iconSource ?? IconOf;
        ActiveDrag = activeDrag ?? (() => InputDispatcher.Instance?.ActiveDragPayload);
        Name = "hotbar";
        SetBounds(MainHudLayout.Hotbar);

        var pageUp = new IconButton(ThemeRoles.IconHotbarPageUp, MainHudLayout.HotbarPageUp) { Name = "page_up" };
        pageUp.Clicked += () => Model.SetPage(Model.Page + 1);
        AddChild(pageUp);

        PageText = HudText.Label("page", ThemeRoles.Text, HorizontalAlignment.Center);
        PageText.SetBounds(MainHudLayout.HotbarPageNumber);
        AddChild(PageText);

        var pageDown = new IconButton(ThemeRoles.IconHotbarPageDown, MainHudLayout.HotbarPageDown) { Name = "page_down" };
        pageDown.Clicked += () => Model.SetPage(Model.Page - 1);
        AddChild(pageDown);

        for (var i = 0; i < Hotbar.SLOTS; i++)
        {
            var slot = new HotbarSlotControl(i, KeyLabels[i], Wedges, () => Model.Page);
            slot.SetBounds(MainHudLayout.HotbarSlot(i));
            slot.UseRequested += Use;
            slot.DetailsRequested += ShowDetails;
            slot.DropRequested += Drop;
            slot.DragStarted += payload => (Outgoing, OutgoingCancelled) = (payload, false);
            slot.DragCancelled += () => OutgoingCancelled = true;
            slot.PointerChanged += Point;
            Slots[i] = slot;
            AddChild(slot);
        }

        Refresh();
    }

    /// <summary>A click or a number key on an entry: the world screen uses it.</summary>
    public event Action<HotbarEntry>? UseRequested;

    /// <summary>A right click on an ability: the world screen opens its castable popup.</summary>
    public event Action<HotbarEntry>? DetailsRequested;

    internal string PageShown => PageText.Text;

    internal HotbarSlotControl SlotAt(int index) => Slots[index];

    /// <summary>Uses the entry of slot <paramref name="index" /> of the page that shows; an empty slot does nothing.</summary>
    public void Use(int index)
    {
        if (Model.Get(index) is { } entry)
            UseRequested?.Invoke(entry);
    }

    //the slots first, then the end of a drag and the entries, so the draw of this frame shows the hotbar of this frame
    public override void Update(GameTime gameTime)
    {
        base.Update(gameTime);
        EndDrag();
        Refresh();
    }

    //the wheel changes the page, as the arrows do: up is the next page
    public override void OnMouseScroll(MouseScrollEvent e)
    {
        Model.SetPage(Model.Page + Math.Sign(e.Delta));
        e.Handled = true;
    }

    //a drop of a slot on the hotbar but not on a slot (a gap, the page column) keeps the slot: a near miss does not clear it
    public override void OnDragDrop(DragDropEvent e)
    {
        if (e.Payload is not HotbarDragPayload)
            return;

        Outgoing = null;
        e.Handled = true;
    }

    public override void Dispose()
    {
        Wedges.Dispose();
        base.Dispose();
    }

    //a drag of a slot that ended with no drop on the hotbar clears the slot; a drop that a modal blocked keeps it
    private void EndDrag()
    {
        if ((Outgoing is not { } outgoing) || ReferenceEquals(ActiveDrag(), outgoing))
            return;

        Outgoing = null;

        if (!OutgoingCancelled)
            Model.Clear(outgoing.Page, outgoing.Index);
    }

    //each slot shows the entry of the page that shows, in its state now; the tooltip follows the entry under the pointer
    private void Refresh()
    {
        if (ShownPage != Model.Page)
        {
            ShownPage = Model.Page;
            PageText.Text = (Model.Page + 1).ToString(CultureInfo.InvariantCulture);
        }

        for (var i = 0; i < Hotbar.SLOTS; i++)
            if (Model.Get(i) is { } entry)
                Slots[i].Show(entry, Model.StateOf(entry), IconSource);
            else
                Slots[i].Show(null, default, IconSource);

        var showTooltip = Hovered is { } hovered && (Model.Get(hovered) is not null);

        if (showTooltip == TooltipShown)
            return;

        TooltipShown = showTooltip;

        if (showTooltip)
            Tooltip.ShowAbove(Slots[Hovered!.Value], () => Lines(Hovered ?? 0));
        else
            Tooltip.Hide();
    }

    private void ShowDetails(int index)
    {
        if (Model.Get(index) is { Kind: not HotbarKind.Item } entry)
            DetailsRequested?.Invoke(entry);
    }

    //a drop from a window sets the entry; a drop from a hotbar slot swaps the two, also from another page
    private void Drop(int index, object? payload)
    {
        switch (payload)
        {
            case AbilityDragPayload ability when (ability.IsSpell ? Model.EntryForSpell(ability.Slot) : Model.EntryForSkill(ability.Slot)) is { } entry:
                Model.Set(Model.Page, index, entry);

                break;

            case ItemDragPayload item when Model.EntryForItem(item.Slot) is { } entry:
                Model.Set(Model.Page, index, entry);

                break;

            case HotbarDragPayload slot:
                Outgoing = null;
                Model.Move(slot.Page, slot.Index, Model.Page, index);

                break;
        }
    }

    private void Point(int index, bool entered)
    {
        if (entered)
            Hovered = index;
        else if (Hovered == index)
            Hovered = null;

        Refresh();
    }

    //the name and the key; an item that the inventory does not have has a line for it
    private IReadOnlyList<TooltipLine> Lines(int index)
    {
        if (Model.Get(index) is not { } entry)
            return [];

        List<TooltipLine> lines = [TooltipLine.Themed(entry.Name, ThemeRoles.Text)];

        if ((entry.Kind == HotbarKind.Item) && Model.StateOf(entry).IsGone)
            lines.Add(TooltipLine.Themed("None in the inventory", ThemeRoles.TextDisabled));

        lines.Add(TooltipLine.Themed($"Key: {KeyLabels[index]}", ThemeRoles.TextDisabled));

        return lines;
    }

    private static IconTexture? IconOf(HotbarEntry entry, HotbarEntryState state)
        => UiRenderer.Instance is not { } renderer
            ? null
            : entry.Kind switch
            {
                HotbarKind.Skill => renderer.GetSkillIcon(state.Sprite),
                HotbarKind.Spell => renderer.GetSpellIcon(state.Sprite),
                _                => HudItemSlotControl.ItemIcon(state.Sprite, state.Color)
            };
}

/// <summary>
///     A slot of the hotbar (BRIG-146): the <see cref="ThemeRoles.HudHotbarSlot" /> part (else the inventory slot part),
///     the icon of the entry, its key at the top left, the count of an item at the bottom right, and the cooldown swipe
///     of an ability. The slot shows dimmed while its ability or item is gone. It reports its input to the hotbar.
/// </summary>
internal sealed class HotbarSlotControl : HudItemSlotControl
{
    private const int TEXT_HEIGHT = 12;

    private readonly ClockWedgeCache Wedges;
    private readonly Func<int> CurrentPage;
    private readonly UILabel KeyText;
    private readonly UILabel CountText;

    //the slot reads the icon again only when the kind, the sprite or the colour changes; it keeps a read that found no
    //icon too, because the renderer does not cache such a read
    private (HotbarKind Kind, ushort Sprite, DisplayColor Color)? IconKey;
    private uint ShownCount;

    public HotbarSlotControl(int index, string keyLabel, ClockWedgeCache wedges, Func<int> currentPage)
        : base(true)
    {
        Index = index;
        Wedges = wedges;
        CurrentPage = currentPage;
        Name = $"slot_{index}";

        KeyText = HudText.Label("key", ThemeRoles.Text);
        KeyText.Text = keyLabel;
        AddChild(KeyText);

        CountText = HudText.Label("count", ThemeRoles.Text, HorizontalAlignment.Right);
        AddChild(CountText);
    }

    /// <summary>The index of the slot, from 0.</summary>
    public int Index { get; }

    /// <summary>The entry that the slot shows, or null.</summary>
    public HotbarEntry? Entry { get; private set; }

    /// <summary>The state of the entry when the slot read it.</summary>
    public HotbarEntryState State { get; private set; }

    internal string KeyShown => KeyText.Text;

    internal string CountShown => CountText.Text;

    internal bool ShowsDimmed => Dimmed;

    protected override string SlotRole => ThemeRoles.HudHotbarSlot;

    protected override string? FallbackSlotRole => ThemeRoles.HudInventorySlot;

    protected override bool Dimmed => Entry is not null && State.IsGone;

    /// <summary>A left click on an entry.</summary>
    public event Action<int>? UseRequested;

    /// <summary>A right click on an entry.</summary>
    public event Action<int>? DetailsRequested;

    /// <summary>A drop on the slot: the index and the payload.</summary>
    public event Action<int, object?>? DropRequested;

    /// <summary>A drag of this slot started.</summary>
    public event Action<HotbarDragPayload>? DragStarted;

    /// <summary>A modal blocked the drop of a drag of this slot.</summary>
    public event Action? DragCancelled;

    /// <summary>The pointer came on the slot (true) or went off it (false).</summary>
    public event Action<int, bool>? PointerChanged;

    /// <summary>Shows <paramref name="entry" /> in <paramref name="state" />, or an empty slot for null.</summary>
    /// <param name="iconSource">The icon of an entry in its state, read only when the icon changes.</param>
    public void Show(HotbarEntry? entry, HotbarEntryState state, Func<HotbarEntry, HotbarEntryState, IconTexture?> iconSource)
    {
        Entry = entry;
        State = state;

        (HotbarKind, ushort, DisplayColor)? key = entry is null ? null : (entry.Kind, state.Sprite, state.Color);

        if (key != IconKey)
        {
            IconKey = key;
            SetIcon(entry is null ? null : iconSource(entry, state));
        }

        var count = entry?.Kind == HotbarKind.Item ? state.Count : 0;

        if (count != ShownCount)
        {
            ShownCount = count;
            CountText.Text = count > 1 ? count.ToString(CultureInfo.InvariantCulture) : string.Empty;
        }
    }

    public override void SetBounds(Rectangle bounds)
    {
        base.SetBounds(bounds);
        KeyText.SetBounds(new Rectangle(2, 0, bounds.Width - 2, TEXT_HEIGHT));
        CountText.SetBounds(new Rectangle(0, bounds.Height - TEXT_HEIGHT, bounds.Width - 1, TEXT_HEIGHT));
    }

    //a click on an empty slot does nothing, and does not reach the world under the hotbar
    public override void OnClick(ClickEvent e)
    {
        if (Entry is null)
        {
            base.OnClick(e);

            return;
        }

        if (e.Button == MouseButton.Left)
            UseRequested?.Invoke(Index);
        else if (e.Button == MouseButton.Right)
            DetailsRequested?.Invoke(Index);
        else
            return;

        e.Handled = true;
    }

    public override void OnDragStart(DragStartEvent e)
    {
        base.OnDragStart(e);

        if (e.Payload is HotbarDragPayload payload)
            DragStarted?.Invoke(payload);
    }

    public override void OnDragCancel()
    {
        DragCancelled?.Invoke();
        base.OnDragCancel();
    }

    protected override bool TakesDrop(object? payload) => payload is AbilityDragPayload or ItemDragPayload or HotbarDragPayload;

    protected override object DragPayloadOf(IconTexture icon) => new HotbarDragPayload(CurrentPage(), Index, icon.Texture);

    //every drop on a slot ends here: one that the slot does not take changes nothing, and does not reach the world
    protected override void HandleDrop(DragDropEvent e)
    {
        DropRequested?.Invoke(Index, e.Payload);
        e.Handled = true;
    }

    protected override void OnPointer(bool entered) => PointerChanged?.Invoke(Index, entered);

    //the swipe lies on the icon, which a pack icon moves by its offset
    protected override void DrawOverIcon(SpriteBatch spriteBatch, Rectangle bounds)
    {
        if ((Entry is null) || (State.Cooldown <= 0))
            return;

        var at = Icon is { } shown ? new Vector2(bounds.X + shown.OffsetX, bounds.Y + shown.OffsetY) : new Vector2(bounds.X, bounds.Y);
        DrawTexture(spriteBatch, Wedges.ForLeft(spriteBatch.GraphicsDevice, State.Cooldown), at, ClockWedgeCache.SwipeColor);
    }
}
