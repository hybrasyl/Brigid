#region
using Brigid.Controls.Components;
using Brigid.Controls.Generic;
using Brigid.Definitions;
using Brigid.Rendering;
using Brigid.ViewModel;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
#endregion

namespace Brigid.Controls.World.Popups;

/// <summary>
///     The editor of a custom chat tab (BRIG-145): a themed <see cref="CenteredModalPanel" /> with a name field and one
///     check box for each <see cref="ChatChannel" />, and OK and Cancel on the bottom bar. It makes a new tab, or changes
///     the name and the channels of one.
///     <para>
///         OK is disabled, and Enter does nothing, while the name is not valid (<see cref="ChatTab.IsValidName" />), names
///         another tab, or no channel is checked. OK and Enter hide the editor and raise <see cref="Confirmed" />. Cancel
///         and Escape hide it. A caller centres the editor with <see cref="CenteredModalPanel.SetViewportBounds" />, and
///         opens it with <see cref="Show(ChatTab?)" />.
///     </para>
/// </summary>
public sealed class ChatTabEditorControl : CenteredModalPanel
{
    private const int PANEL_W = 230;
    private const int ACTION_W = 64;
    private const int GAP = 6;
    private const int LABEL_H = 14;
    private const int FIELD_H = 18;
    private const int ROW_H = 16;
    private const int COLUMNS = 2;
    private static readonly int Rows = (Enum.GetValues<ChatChannel>().Length + COLUMNS - 1) / COLUMNS;
    private static readonly int ContentHeight = GAP + LABEL_H + GAP + FIELD_H + GAP + LABEL_H + GAP + Rows * ROW_H + GAP;

    private readonly List<(ChatChannel Channel, CheckBox Box, UILabel Label)> Channels = [];
    private readonly UILabel ChannelsLabel;
    private readonly InputSlotPane FieldPane;
    private readonly UILabel NameLabel;
    private readonly Func<string, ChatTab?, bool> IsNameFree;
    private ChatTab? Editing;

    /// <param name="isNameFree">True when a name is free for the edited tab (null for a new tab): the chat box's rule.</param>
    public ChatTabEditorControl(Func<string, ChatTab?, bool> isNameFree)
        //SetContentHeight sets the height
        : base("New chat tab", PANEL_W, 0, useTheme: true)
    {
        IsNameFree = isNameFree;
        Name = "chat_tab_editor";
        IsModal = true;

        //Cancel takes the place of Close; OK sits left of it
        CloseButton.Visible = false;
        CancelButton = AddBottomBarButton("Cancel", ACTION_W, Hide, "cancel");
        OkButton = AddBottomBarButton("OK", ACTION_W, Confirm, "ok");
        OkButton.SetEnabled(false);

        NameLabel = NewLabel("name_label", "Name");
        FieldPane = new InputSlotPane { Name = "name_field" };

        NameBox = new UITextBox
        {
            Name = "name",
            X = 0,
            Y = 0,
            Height = FIELD_H,
            MaxLength = ChatTab.MAX_NAME,
            FocusedBackgroundColor = DialogPalette.InputFocus
        };
        NameBox.SetForegroundRole(ThemeRoles.InputText);
        FieldPane.AddChild(NameBox);
        AddChild(FieldPane);

        ChannelsLabel = NewLabel("channels_label", "Channels");

        foreach (var channel in Enum.GetValues<ChatChannel>())
        {
            var id = channel.ToString().ToLowerInvariant();
            var box = new CheckBox { Name = $"{id}_box" };
            var label = NewLabel($"{id}_label", LabelOf(channel));

            AddChild(box);
            Channels.Add((channel, box, label));
        }

        SetContentHeight(ContentHeight);
        LayoutContent();
    }

    /// <summary>The name field.</summary>
    public UITextBox NameBox { get; }

    public TextButton OkButton { get; }
    public TextButton CancelButton { get; }

    /// <summary>Raised when the player confirms: the tab that the editor changed (null for a new tab), and the new tab.</summary>
    public event Action<ChatTab?, ChatTab>? Confirmed;

    /// <summary>True when OK confirms: a valid custom tab (<see cref="ChatTab.IsValidCustom" />) whose name no other tab has.</summary>
    public bool CanConfirm => Candidate().IsValidCustom && IsNameFree(NameBox.Text, Editing);

    /// <summary>The title of the editor: "New chat tab" or "Edit chat tab".</summary>
    internal string Title { get; private set; } = "New chat tab";

    /// <summary>The check box of <paramref name="channel" />.</summary>
    public CheckBox BoxOf(ChatChannel channel) => Channels.Single(entry => entry.Channel == channel).Box;

    /// <summary>Opens the editor for a new tab.</summary>
    public override void Show() => Show(null);

    /// <summary>
    ///     Opens the editor for <paramref name="tab" />, with its name and channels, or for a new tab with an empty name
    ///     and no channel.
    /// </summary>
    public void Show(ChatTab? tab)
    {
        Editing = tab;
        Title = tab is null ? "New chat tab" : "Edit chat tab";
        SetTitle(Title);
        NameBox.Text = tab?.Name ?? string.Empty;
        NameBox.ResetUndoHistory();

        foreach (var (channel, box, _) in Channels)
            box.Checked = tab?.Channels.Contains(channel) ?? false;

        OkButton.SetEnabled(CanConfirm);
        base.Show();
        NameBox.IsFocused = true;
    }

    public override void Hide()
    {
        if (!Visible)
            return;

        base.Hide();
        NameBox.IsFocused = false;
    }

    public override void Update(GameTime gameTime)
    {
        if (Visible && (OkButton.Enabled != CanConfirm))
            OkButton.SetEnabled(CanConfirm);

        base.Update(gameTime);
    }

    public override void OnKeyDown(KeyDownEvent e)
    {
        //Alt+Enter changes the window size; it must not confirm the editor
        if ((e.Key == Keys.Enter) && !e.Alt)
        {
            Confirm();
            e.Handled = true;

            return;
        }

        //Escape hides the editor, and the base takes every other key
        base.OnKeyDown(e);
    }

    //the name above its field, then the channels in two columns, all in the content area
    protected override void LayoutContent()
    {
        var content = ContentBounds;
        var y = content.Y + GAP;

        NameLabel.SetBounds(new Rectangle(content.X, y, content.Width, LABEL_H));
        y += LABEL_H + GAP;

        FieldPane.X = content.X;
        FieldPane.Y = y;
        FieldPane.Width = content.Width;
        FieldPane.Height = FIELD_H;
        NameBox.Width = content.Width;
        y += FIELD_H + GAP;

        ChannelsLabel.SetBounds(new Rectangle(content.X, y, content.Width, LABEL_H));
        y += LABEL_H + GAP;

        var columnWidth = content.Width / COLUMNS;

        for (var i = 0; i < Channels.Count; i++)
        {
            var (_, box, label) = Channels[i];
            var x = content.X + (i / Rows) * columnWidth;
            var rowY = y + (i % Rows) * ROW_H;

            box.X = x;
            box.Y = rowY + (ROW_H - CheckBox.DEFAULT_SIZE) / 2;
            label.SetBounds(new Rectangle(x + CheckBox.DEFAULT_SIZE + 4, rowY, columnWidth - CheckBox.DEFAULT_SIZE - 4, ROW_H));
        }
    }

    private void Confirm()
    {
        if (!CanConfirm)
            return;

        var tab = Candidate();
        var edited = Editing;

        Hide();
        Confirmed?.Invoke(edited, tab);
    }

    //the tab that OK makes from the field and the check boxes
    private ChatTab Candidate()
        => new(NameBox.Text, Channels.Where(entry => entry.Box.Checked).Select(entry => entry.Channel).ToHashSet());

    private UILabel NewLabel(string name, string text)
    {
        var label = new UILabel
        {
            Name = name,
            Text = text,
            Height = LABEL_H,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false
        };

        label.SetForegroundRole(ThemeRoles.Text);
        AddChild(label);

        return label;
    }

    private static string LabelOf(ChatChannel channel) => channel == ChatChannel.Npc ? "NPC" : channel.ToString();
}
