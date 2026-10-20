#region
using Brigid.Controls.Components;
using Brigid.Controls.Scrolling;
using Brigid.Definitions;
using Brigid.Rendering;
using Chaos.DarkAges.Definitions;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
#endregion

namespace Brigid.Controls.Generic;

/// <summary>
///     A debug panel that shows one of each asset-free primitive in each state, and a chip for each theme colour, each with
///     its <c>ui_theme</c> role and its logical size (BRIG-122). With no theme pack it is the reference sheet for the flat
///     look. With a theme it shows that theme's parts and colours, and a colour that the theme does not set says "flat". It
///     needs no dat data.
///     <para>
///         Ctrl+G opens and closes it while the F11 overlay is on, on the lobby and the world screens. <c>ChaosGame</c>
///         handles the key before the screen gets it. Escape closes an open drop-down list, else the gallery. The gallery
///         takes every other key. The samples take no mouse input, so each one keeps the state that it shows. The second
///         text field, the drop-down and the scroll view are live.
///     </para>
/// </summary>
public sealed class PrimitiveGallery : CenteredModalPanel
{
    private const string PANEL_NAME = "primitivegallery";
    private const int PANEL_W = 620;
    private const int PANEL_H = 440;
    private const int COLUMN_B = 316;
    private const int COLUMN_W = 292;
    private const int SECTION_H = 52;
    private const int HEADER_H = 14;
    private const int CAPTION_H = 12;
    private const int SAMPLE_W = 70;
    private const int SAMPLE_H = 20;
    private const int CELL_W = 76;
    private const int FIELD_W = 140;
    private const int BOX_W = 140;
    private const int BOX_H = 70;
    private const int CHIP = 12;
    private const int CHIP_ROW = 16;
    private const int CHIP_COLUMN = 146;
    private const int STATUS_SIZE = 16;
    private const int STATUS_CELL_W = 36;
    private const string STATUS_PREFIX = "icon.status.";


    //where each colour chip is drawn, in panel coordinates, in the order of ThemeRoles.Colours; with no theme colour, a chip shows the flat value of its role
    private readonly Rectangle[] ChipRects;

    private readonly DropDown DropDownSample;

    private PrimitiveGallery()
        : base("Primitive Gallery", PANEL_W, PANEL_H, useTheme: true)
    {
        Name = PANEL_NAME;
        IsModal = true;

        var content = ContentBounds;
        var ax = content.X + 4;
        var bx = content.X + COLUMN_B;
        var y = content.Y + 4;

        //buttons: normal, hover, pressed, disabled
        AddHeader(new UILabel { Name = "buttons_header", Text = "TextButton (70 by 20)" }, ax, y);
        var hover = new TextButton("Hover", SAMPLE_W, SAMPLE_H) { Name = "button_hover" };
        var pressed = new TextButton("Pressed", SAMPLE_W, SAMPLE_H) { Name = "button_pressed" };
        var disabled = new TextButton("Disabled", SAMPLE_W, SAMPLE_H) { Name = "button_disabled" };
        hover.ShowState(true, false);
        pressed.ShowState(true, true);
        disabled.SetEnabled(false);
        AddRow(y, ax, new TextButton("Normal", SAMPLE_W, SAMPLE_H) { Name = "button_normal" }, hover, pressed, disabled);
        AddCaption(new UILabel { Name = "button_normal_role", Text = ThemeRoles.ButtonNormal }, ax, y);
        AddCaption(new UILabel { Name = "button_hover_role", Text = ThemeRoles.ButtonHover }, ax + CELL_W, y);
        AddCaption(new UILabel { Name = "button_pressed_role", Text = ThemeRoles.ButtonPressed }, ax + 2 * CELL_W, y);
        AddCaption(new UILabel { Name = "button_disabled_role", Text = ThemeRoles.ButtonDisabled }, ax + 3 * CELL_W, y);
        y += SECTION_H;

        //tabs: idle and selected
        AddHeader(new UILabel { Name = "tabs_header", Text = "SelectableTab (70 by 20)" }, ax, y);
        AddRow(
            y,
            ax,
            new SelectableTab("Idle", SAMPLE_W, SAMPLE_H) { Name = "tab_idle" },
            new SelectableTab("Selected", SAMPLE_W, SAMPLE_H) { Name = "tab_selected", IsSelected = true });
        AddCaption(new UILabel { Name = "tab_idle_role", Text = ThemeRoles.TabNormal }, ax, y);
        AddCaption(new UILabel { Name = "tab_selected_role", Text = ThemeRoles.TabSelected }, ax + CELL_W, y);
        y += SECTION_H;

        //check boxes: normal, hover, checked
        AddHeader(new UILabel { Name = "checkboxes_header", Text = "CheckBox (14 by 14)" }, ax, y);
        var hoverBox = new CheckBox { Name = "checkbox_hover" };
        hoverBox.ShowState(true);
        AddRow(y, ax, new CheckBox { Name = "checkbox_normal" }, hoverBox, new CheckBox { Name = "checkbox_checked", Checked = true });
        AddCaption(new UILabel { Name = "checkbox_normal_role", Text = ThemeRoles.CheckBoxNormal }, ax, y);
        AddCaption(new UILabel { Name = "checkbox_hover_role", Text = ThemeRoles.CheckBoxHover }, ax + CELL_W, y);
        AddCaption(new UILabel { Name = "checkbox_checked_role", Text = ThemeRoles.CheckBoxMark }, ax + 2 * CELL_W, y);
        y += SECTION_H;

        //slider: track, fill and thumb
        AddHeader(new UILabel { Name = "slider_header", Text = "SliderControl (146 by 14, thumb 8 by 14)" }, ax, y);
        var slider = new SliderControl(146, 14) { Name = "slider" };
        slider.SetValue(6);
        AddRow(y, ax, slider);
        AddCaption(
            new UILabel { Name = "slider_role", Text = $"{ThemeRoles.SliderTrack}, {ThemeRoles.SliderFill}, {ThemeRoles.SliderThumb}" },
            ax,
            y,
            COLUMN_W);
        y += SECTION_H;

        //input slots: a field with text, and a live field to click for the focused slot
        AddHeader(new UILabel { Name = "slots_header", Text = "InputSlotPane (fields 140 by 18)" }, ax, y);
        var slots = new InputSlotPane { Name = "slots", X = ax, Y = y + HEADER_H, Width = 2 * FIELD_W + 6, Height = 18 };
        var field = new UITextBox { Name = "field", X = 0, Y = 0, Width = FIELD_W, Height = 18, Text = "input.text", IsHitTestVisible = false };
        field.SetForegroundRole(ThemeRoles.InputText);
        var liveField = new UITextBox { Name = "live_field", X = FIELD_W + 6, Y = 0, Width = FIELD_W, Height = 18, FocusedBackgroundColor = DialogPalette.InputFocus };
        liveField.SetForegroundRole(ThemeRoles.InputText);
        slots.AddChild(field);
        slots.AddChild(liveField);
        AddChild(slots);
        AddCaption(new UILabel { Name = "slot_role", Text = ThemeRoles.InputSlot }, ax, y, FIELD_W);
        AddCaption(new UILabel { Name = "slot_focus_role", Text = $"{ThemeRoles.InputSlotFocus} (click)" }, ax + FIELD_W + 6, y, FIELD_W);
        y += SECTION_H;

        //drop-down (live), stepper and swatches
        AddHeader(new UILabel { Name = "misc_header", Text = "DropDown 110x18, Stepper 100x18, ColorSwatch 16x16" }, ax, y);
        DropDownSample = new DropDown(["Entry", "Disabled entry"], 110, 18) { Name = "dropdown", X = ax, Y = y + HEADER_H, ZIndex = 1 };
        DropDownSample.SetEntryEnabled(1, false);
        AddChild(DropDownSample);
        var stepper = new Stepper(100, 18) { Name = "stepper", X = ax + 116, Y = y + HEADER_H, IsHitTestVisible = false };
        stepper.SetValue("Hair 3/18");
        AddChild(stepper);
        AddChild(new ColorSwatch(DialogPalette.Text) { Name = "swatch", X = ax + 224, Y = y + HEADER_H + 1, Width = 16, Height = 16, IsHitTestVisible = false });
        AddChild(
            new ColorSwatch(DialogPalette.Title)
            {
                Name = "swatch_selected",
                X = ax + 246,
                Y = y + HEADER_H + 1,
                Width = 16,
                Height = 16,
                IsSelected = true,
                IsHitTestVisible = false
            });
        AddCaption(new UILabel { Name = "dropdown_role", Text = $"button.*, {ThemeRoles.DropDownList}" }, ax, y, 110);
        AddCaption(new UILabel { Name = "stepper_role", Text = "button.*" }, ax + 116, y, 100);
        AddCaption(new UILabel { Name = "swatch_role", Text = ThemeRoles.TextSelected }, ax + 224, y, COLUMN_W - 224);
        y += SECTION_H;

        //the display title: the title font, and the title art in its place
        AddHeader(new UILabel { Name = "title_header", Text = "DisplayTitle (140 by 24)" }, ax, y);
        AddChild(new DisplayTitle { Name = "display_title", Text = "Brigid", Size = 20, X = ax, Y = y + HEADER_H, Width = BOX_W, Height = 24 });
        AddChild(
            new DisplayTitle
            {
                Name = "display_title_art",
                Text = "Brigid",
                Size = 20,
                ArtRole = ThemeRoles.MainMenuTitle,
                X = ax + BOX_W + 6,
                Y = y + HEADER_H,
                Width = BOX_W,
                Height = 24
            });
        AddCaption(new UILabel { Name = "title_font_role", Text = $"font \"{ThemeRoles.TitleFont}\"" }, ax, y + 4, BOX_W);
        AddCaption(new UILabel { Name = "title_art_role", Text = ThemeRoles.MainMenuTitle }, ax + BOX_W + 6, y + 4, BOX_W);

        //column B: a framed panel and a live scroll view side by side
        var by = content.Y + 4;
        AddHeader(new UILabel { Name = "frame_header", Text = "FramePanel, ScrollView (140 by 70)" }, bx, by);
        AddChild(new FramePanel { Name = "frame", X = bx, Y = by + HEADER_H, Width = BOX_W, Height = BOX_H, UseTheme = true, IsHitTestVisible = false });

        var scroll = new ScrollView { Name = "scroll", X = bx + BOX_W + 6, Y = by + HEADER_H, Width = BOX_W, Height = BOX_H };
        var scrollText = new UILabel
        {
            Name = "scroll_text",
            WordWrap = true,
            IsHitTestVisible = false,
            Text = "A scroll view with more text than it shows, so that the bar has a thumb to draw. Scroll it with the wheel."
        };
        scrollText.SetForegroundRole(ThemeRoles.Text);
        scroll.AddChild(scrollText);
        AddChild(scroll);
        scrollText.Width = scroll.ContentWidth;
        scrollText.Height = scroll.ContentHeight;
        scrollText.Text = scrollText.Text;
        scroll.SetSource(new LabelScrollSource(scrollText));
        scroll.Sync();

        var boxCaptionY = by + BOX_H - SAMPLE_H;
        AddCaption(new UILabel { Name = "frame_role", Text = ThemeRoles.FramePanel }, bx, boxCaptionY, BOX_W);
        AddCaption(new UILabel { Name = "scroll_role", Text = "scrollbar.track, .thumb, .up, .down" }, bx + BOX_W + 6, boxCaptionY, BOX_W);
        AddCaption(
            new UILabel
            {
                Name = "scroll_states_role",
                WordWrap = true,
                Height = 2 * CAPTION_H,
                Text = $"{ThemeRoles.ScrollThumbHover}, {ThemeRoles.ScrollThumbPressed}, {ThemeRoles.ScrollUpPressed}, "
                       + $"{ThemeRoles.ScrollDownPressed}: use the bar"
            },
            bx,
            boxCaptionY + CAPTION_H,
            COLUMN_W);
        by += HEADER_H + BOX_H + 3 * CAPTION_H + 12;

        //colour chips, two columns; a colour that the theme does not set says "flat"
        AddHeader(new UILabel { Name = "colours_header", Text = "Colours (the theme's, else the flat value)" }, bx, by);
        by += HEADER_H + 2;
        ChipRects = new Rectangle[ThemeRoles.Colours.Length];

        for (var i = 0; i < ThemeRoles.Colours.Length; i++)
            ChipRects[i] = new Rectangle(bx + (i % 2) * CHIP_COLUMN, by + (i / 2) * CHIP_ROW + 1, CHIP, CHIP);

        AddChipLabel(new UILabel { Name = "chip_text" }, 0);
        AddChipLabel(new UILabel { Name = "chip_text_disabled" }, 1);
        AddChipLabel(new UILabel { Name = "chip_text_selected" }, 2);
        AddChipLabel(new UILabel { Name = "chip_text_title" }, 3);
        AddChipLabel(new UILabel { Name = "chip_text_error" }, 4);
        AddChipLabel(new UILabel { Name = "chip_divider" }, 5);
        AddChipLabel(new UILabel { Name = "chip_border" }, 6);
        AddChipLabel(new UILabel { Name = "chip_row_hover" }, 7);
        AddChipLabel(new UILabel { Name = "chip_row_selected" }, 8);
        AddChipLabel(new UILabel { Name = "chip_input_text" }, 9);
        AddChipLabel(new UILabel { Name = "chip_input_focus" }, 10);
        by += (ThemeRoles.Colours.Length + 1) / 2 * CHIP_ROW + 8;

        //the social status icons: the theme's, else Brigid's own; an empty box has neither (the gallery reads no dat file)
        AddHeader(new UILabel { Name = "status_header", Text = "StatusIcon (16 by 16)" }, bx, by);
        AddStatusIcon(new StatusIcon { Name = "status_awake" }, SocialStatus.Awake, bx, by);
        AddStatusIcon(new StatusIcon { Name = "status_do_not_disturb" }, SocialStatus.DoNotDisturb, bx, by);
        AddStatusIcon(new StatusIcon { Name = "status_daydreaming" }, SocialStatus.DayDreaming, bx, by);
        AddStatusIcon(new StatusIcon { Name = "status_need_group" }, SocialStatus.NeedGroup, bx, by);
        AddStatusIcon(new StatusIcon { Name = "status_grouped" }, SocialStatus.Grouped, bx, by);
        AddStatusIcon(new StatusIcon { Name = "status_lone_hunter" }, SocialStatus.LoneHunter, bx, by);
        AddStatusIcon(new StatusIcon { Name = "status_group_hunting" }, SocialStatus.GroupHunting, bx, by);
        AddStatusIcon(new StatusIcon { Name = "status_need_help" }, SocialStatus.NeedHelp, bx, by);
        AddCaption(
            new UILabel
            {
                Name = "status_roles",
                WordWrap = true,
                Height = 2 * CAPTION_H,
                Text = $"{STATUS_PREFIX}*, left to right: {string.Join(", ", ThemeRoles.StatusIcons.Select(role => role[STATUS_PREFIX.Length..]))}"
            },
            bx,
            by - 2,
            COLUMN_W);
        by += HEADER_H + SAMPLE_H + 2 * CAPTION_H + 2;

        //a live popup menu: the button opens it below the button, and the button or a pick closes it
        AddHeader(new UILabel { Name = "popup_header", Text = "PopupMenu (fits its text, rows 14)" }, bx, by);
        var popupMenu = new PopupMenu(3) { Name = "popup_menu", X = bx, Y = by + HEADER_H + SAMPLE_H, ZIndex = 2 };
        var openMenu = new TextButton("Open", SAMPLE_W, SAMPLE_H) { Name = "popup_open", X = bx, Y = by + HEADER_H };
        openMenu.Clicked += () =>
        {
            if (popupMenu.Visible)
                popupMenu.Hide();
            else
                popupMenu.Show(openMenu.X, openMenu.Y + SAMPLE_H, "Menu title", [("First entry", static () => { }), ("A longer second entry", static () => { }), ("Third", static () => { })]);
        };
        AddChild(openMenu);
        AddChild(popupMenu);

        //a menu that stays open stays on the control stack
        OnClose += popupMenu.Hide;
        AddCaption(new UILabel { Name = "popup_role", Text = $"{ThemeRoles.FramePopup}, {ThemeRoles.RowHover} (click)" }, bx + SAMPLE_W + 6, by - SAMPLE_H + 2, COLUMN_W - SAMPLE_W - 6);
        by += HEADER_H + SAMPLE_H + 4;

        //the roles that a small sample cannot show
        AddHeader(new UILabel { Name = "not_shown_header", Text = "Not shown here" }, bx, by);
        AddCaption(
            new UILabel
            {
                Name = "not_shown",
                WordWrap = true,
                Height = 2 * CAPTION_H,
                Text = $"{ThemeRoles.MainMenuBackground}, {ThemeRoles.StoryBackground} (Story), "
                       + $"{ThemeRoles.FrameSign} (360 by 180, the wooden sign)"
            },
            bx,
            by - SAMPLE_H,
            COLUMN_W);

        OnClose += Remove;
    }

    /// <summary>
    ///     Opens the gallery on <paramref name="root" />, or closes it when it is open there. Does nothing with no root (the
    ///     launcher screen). Opening it takes the focus from a text box (the chat), so that the gallery gets the keys and
    ///     the clicks.
    /// </summary>
    public static void Toggle(UIPanel? root)
    {
        if (root is null)
            return;

        if (root.FindChild<PrimitiveGallery>(PANEL_NAME) is { } open)
        {
            open.Hide();

            return;
        }

        UITextBox.CurrentlyFocused?.IsFocused = false;
        InputDispatcher.Instance?.ClearExplicitFocus();

        var gallery = new PrimitiveGallery { ZIndex = 50 };
        root.AddChild(gallery);
        gallery.Show();
    }

    //the gallery is made again on each open, so a close removes it from its parent
    private void Remove() => Parent?.RemoveChild(PANEL_NAME);

    //a section header, in the title colour
    private void AddHeader(UILabel label, int x, int y)
    {
        label.X = x;
        label.Y = y;
        label.Width = COLUMN_W;
        label.Height = HEADER_H;
        label.IsHitTestVisible = false;
        label.SetForegroundRole(ThemeRoles.TextTitle);
        AddChild(label);
    }

    //places sample controls in a row below a header, one cell each; the samples take no mouse input
    private void AddRow(int y, int x, params UIElement[] samples)
    {
        for (var i = 0; i < samples.Length; i++)
        {
            var sample = samples[i];

            sample.X = x + i * CELL_W;
            sample.Y = y + HEADER_H + (SAMPLE_H - sample.Height) / 2;
            sample.IsHitTestVisible = false;
            AddChild(sample);
        }
    }

    //a role caption below a sample; a caption that sets no height is one line
    private void AddCaption(UILabel label, int x, int y, int width = CELL_W - 2)
    {
        label.X = x;
        label.Y = y + HEADER_H + SAMPLE_H + 2;
        label.Width = width;

        if (label.Height == 0)
            label.Height = CAPTION_H;

        label.IsHitTestVisible = false;
        label.SetForegroundRole(ThemeRoles.TextDisabled);
        AddChild(label);
    }

    //a status icon in its cell of the status row, outlined so that an icon with no art still shows its box
    private void AddStatusIcon(StatusIcon icon, SocialStatus status, int x, int y)
    {
        icon.Status = status;
        icon.X = x + (int)status * STATUS_CELL_W;
        icon.Y = y + HEADER_H + (SAMPLE_H - STATUS_SIZE) / 2;
        icon.Width = STATUS_SIZE;
        icon.Height = STATUS_SIZE;
        icon.BorderColor = DialogPalette.Divider;
        icon.IsHitTestVisible = false;
        AddChild(icon);
    }

    //a role name beside its colour chip, marked "flat" when the theme does not set the colour
    private void AddChipLabel(UILabel label, int index)
    {
        var chip = ChipRects[index];
        var role = ThemeRoles.Colours[index];

        label.Text = TryGetThemeColor(role, out _) ? role : $"{role} (flat)";
        label.X = chip.Right + 4;
        label.Y = chip.Y - 1;
        label.Width = CHIP_COLUMN - CHIP - 8;
        label.Height = CHIP_ROW - 2;
        label.IsHitTestVisible = false;
        label.SetForegroundRole(ThemeRoles.Text);
        AddChild(label);
    }

    public override void Draw(SpriteBatch spriteBatch)
    {
        base.Draw(spriteBatch);

        if (!Visible)
            return;

        for (var i = 0; i < ThemeRoles.Colours.Length; i++)
        {
            var chip = ChipRects[i];
            var screen = new Rectangle(ScreenX + chip.X, ScreenY + chip.Y, chip.Width, chip.Height);

            DrawRectClipped(spriteBatch, screen, ThemeColor(ThemeRoles.Colours[i]));
            DrawBorderClipped(spriteBatch, screen, DialogPalette.Text);
        }
    }

    public override void OnKeyDown(KeyDownEvent e)
    {
        //Escape closes an open drop-down list first
        if ((e.Key == Keys.Escape) && DropDownSample.IsOpen)
        {
            DropDownSample.Close();
            e.Handled = true;

            return;
        }

        //the base closes the gallery on Escape and takes every other key
        base.OnKeyDown(e);
    }
}
