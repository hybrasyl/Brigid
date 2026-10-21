#region
using Brigid.Controls.Components;
using Brigid.Controls.Generic;
using Brigid.Definitions;
using Brigid.Rendering;
using DALib.Networking.Packets.Server;
#endregion

namespace Brigid.Controls.World.Popups;

/// <summary>The three modes of the group recruit form.</summary>
public enum GroupRecruitMode
{
    /// <summary>You have no group box: Begin makes one.</summary>
    OwnerNew,

    /// <summary>You have a group box: Modify changes it and Reset removes it.</summary>
    OwnerEdit,

    /// <summary>The group box of another player: the fields are read-only, and Join asks to join the group.</summary>
    Viewer
}

/// <summary>
///     The group recruit form (BRIG-126 step 3c), in place of the legacy <c>_ngcdlg1</c> prefab: the title, the note, the
///     level range, and a row for each class with the count that the group wants. The viewer also shows the count that
///     the group has now, and the totals. The level and count fields take digits only.
///     <para>
///         The form has no buttons. Its host puts the actions of the current <see cref="Mode" /> on its bottom bar and calls
///         <see cref="BeginBox" />, <see cref="ModifyBox" />, <see cref="RemoveBox" /> or <see cref="RequestJoin" />. An
///         action that ends the form raises <see cref="OnClose" />, and the host then hides. Two hosts use the form: the
///         Recruit tab of the group window, and the group box viewer.
///     </para>
/// </summary>
public sealed class GroupRecruitPanel : UIPanel
{
    private const int NUM_CLASSES = 5;
    private const int LABEL_W = 52;
    private const int FIELD_H = 18;
    private const int COUNT_W = 28;
    private const int TO_W = 20;
    private const int NOTE_Y = FIELD_H + 4;
    private const int NOTE_H = 3 * TextRenderer.CHAR_HEIGHT + 4;
    private const int LEVEL_Y = NOTE_Y + NOTE_H + 4;
    private const int HEADER_Y = LEVEL_Y + FIELD_H + 4;
    private const int HEADER_H = 14;
    private const int CLASS_Y = HEADER_Y + HEADER_H + 2;
    private const int CLASS_ROW_H = FIELD_H + 2;
    private const int TOTAL_Y = CLASS_Y + NUM_CLASSES * CLASS_ROW_H;
    private const int WANTED_X = 100;
    private const int CURRENT_X = 170;
    private const int COLUMN_W = 64;

    /// <summary>The width of the form.</summary>
    public const int FORM_W = 260;

    /// <summary>The height of the form.</summary>
    public const int FORM_H = TOTAL_Y + FIELD_H;

    private static readonly string[] ClassNames = ["Warrior", "Wizard", "Rogue", "Priest", "Monk"];

    private readonly UITextBox[] WantedFields = new UITextBox[NUM_CLASSES];
    private readonly UILabel[] CurrentLabels = new UILabel[NUM_CLASSES];
    private readonly UILabel CurrentHeader;
    private readonly UILabel TotalLabel;
    private readonly UILabel TotalWantedLabel;
    private readonly UILabel TotalCurrentLabel;
    private readonly UITextBox[] AllFields;

    private string? ViewerSourceName;

    public UITextBox TitleField { get; }
    public UITextBox ExtraField { get; }
    public UITextBox MinLevelField { get; }
    public UITextBox MaxLevelField { get; }

    /// <summary>The current mode. The host shows the actions of this mode.</summary>
    public GroupRecruitMode Mode { get; private set; }

    /// <summary>Raised when the mode changes, so that the host can show the actions of the new mode.</summary>
    public event Action? ModeChanged;

    public event CloseHandler? OnClose;
    public event CreateGroupBoxHandler? OnCreateGroupBox;
    public event RemoveGroupBoxHandler? OnRemoveGroupBox;
    public event RequestJoinHandler? OnRequestJoin;

    public GroupRecruitPanel()
    {
        Width = FORM_W;
        Height = FORM_H;
        IsPassThrough = true;

        AddCaption("title_caption", "Title", 0, 0, LABEL_W);
        AddCaption("note_caption", "Note", 0, NOTE_Y, LABEL_W);
        AddCaption("level_caption", "Level", 0, LEVEL_Y, LABEL_W);
        AddCaption("level_to", "to", LABEL_W + COUNT_W, LEVEL_Y, TO_W, HorizontalAlignment.Center);

        AddCaption("class_header", "Class", 0, HEADER_Y, LABEL_W, height: HEADER_H, role: ThemeRoles.TextTitle);
        AddCaption("wanted_header", "Wanted", WANTED_X, HEADER_Y, COLUMN_W, height: HEADER_H, role: ThemeRoles.TextTitle);
        CurrentHeader = AddCaption("current_header", "In group", CURRENT_X, HEADER_Y, COLUMN_W, height: HEADER_H, role: ThemeRoles.TextTitle);

        for (var i = 0; i < NUM_CLASSES; i++)
        {
            var rowY = CLASS_Y + i * CLASS_ROW_H;
            AddCaption($"class_{i}", ClassNames[i], 0, rowY, WANTED_X);
            CurrentLabels[i] = AddCaption($"current_{i}", string.Empty, CURRENT_X, rowY, COUNT_W);
        }

        TotalLabel = AddCaption("total_caption", "Total", 0, TOTAL_Y, WANTED_X);
        TotalWantedLabel = AddCaption("total_wanted", string.Empty, WANTED_X, TOTAL_Y, COUNT_W);
        TotalCurrentLabel = AddCaption("total_current", string.Empty, CURRENT_X, TOTAL_Y, COUNT_W);

        //the fields draw on input slots; the pane covers the form and takes no mouse input itself
        var fields = new InputSlotPane
        {
            Name = "fields",
            Width = FORM_W,
            Height = FORM_H,
            IsPassThrough = true
        };

        TitleField = AddField(fields, "group_title", LABEL_W, 0, FORM_W - LABEL_W, FIELD_H, 18);
        ExtraField = AddField(fields, "note", LABEL_W, NOTE_Y, FORM_W - LABEL_W, NOTE_H, 60);
        ExtraField.IsMultiLine = true;
        ExtraField.ClampToVisibleArea = true;
        MinLevelField = AddField(fields, "level_min", LABEL_W, LEVEL_Y, COUNT_W, FIELD_H, 2, true);
        MaxLevelField = AddField(fields, "level_max", LABEL_W + COUNT_W + TO_W, LEVEL_Y, COUNT_W, FIELD_H, 2, true);

        for (var i = 0; i < NUM_CLASSES; i++)
            WantedFields[i] = AddField(fields, $"wanted_{i}", WANTED_X, CLASS_Y + i * CLASS_ROW_H, COUNT_W, FIELD_H, 2, true);

        AddChild(fields);

        AllFields = [TitleField, ExtraField, MinLevelField, MaxLevelField, .. WantedFields];

        ShowAsOwner();
    }

    private UILabel AddCaption(
        string name,
        string text,
        int x,
        int y,
        int width,
        HorizontalAlignment alignment = HorizontalAlignment.Left,
        int height = FIELD_H,
        string role = ThemeRoles.Text)
    {
        var label = new UILabel
        {
            Name = name,
            X = x,
            Y = y,
            Width = width,
            Height = height,
            Text = text,
            HorizontalAlignment = alignment,
            VerticalAlignment = VerticalAlignment.Center,
            PaddingLeft = 0,
            PaddingRight = 0,
            IsHitTestVisible = false
        };
        label.SetForegroundRole(role);
        AddChild(label);

        return label;
    }

    private static UITextBox AddField(
        UIPanel pane,
        string name,
        int x,
        int y,
        int width,
        int height,
        int maxLength,
        bool digitsOnly = false)
    {
        var field = new UITextBox
        {
            Name = name,
            X = x,
            Y = y,
            Width = width,
            Height = height,
            MaxLength = maxLength,
            DigitsOnly = digitsOnly,
            IsTabStop = true,
            FocusedBackgroundColor = DialogPalette.InputFocus
        };
        field.SetForegroundRole(ThemeRoles.InputText);
        pane.AddChild(field);

        return field;
    }

    /// <summary>The count that the viewer shows for each class as in the group now. Internal for the tests.</summary>
    internal IEnumerable<(string Current, bool Visible)> CurrentCounts
        => CurrentLabels.Select(label => (label.Text, label.Visible));

    /// <summary>The wanted count field of each class. Internal for the tests.</summary>
    internal IReadOnlyList<UITextBox> WantedCountFields => WantedFields;

    /// <summary>Sends the form as a new group box, and ends the form.</summary>
    public void BeginBox()
    {
        SendCreateOrModify();
        OnClose?.Invoke();
    }

    /// <summary>
    ///     Sends the form as a change to your group box. The form stays open, as in retail, so that the owner sees the values
    ///     that the server sends back (docs/research/group-ui-original-re.md §2.2).
    /// </summary>
    public void ModifyBox() => SendCreateOrModify();

    /// <summary>Removes your group box on the server, and ends the form.</summary>
    public void RemoveBox()
    {
        OnRemoveGroupBox?.Invoke();
        OnClose?.Invoke();
    }

    /// <summary>Asks to join the group of the viewed group box, and ends the form.</summary>
    public void RequestJoin()
    {
        if (ViewerSourceName is not null)
            OnRequestJoin?.Invoke(ViewerSourceName);

        OnClose?.Invoke();
    }

    private void SendCreateOrModify()
        => OnCreateGroupBox?.Invoke(
            TitleField.Text,
            ExtraField.Text,
            ParseByte(MinLevelField, 1),
            ParseByte(MaxLevelField, 99),
            ParseByte(WantedFields[0]),
            ParseByte(WantedFields[1]),
            ParseByte(WantedFields[2]),
            ParseByte(WantedFields[3]),
            ParseByte(WantedFields[4]));

    //the field takes at most two digits, so a non-empty value always parses
    private static byte ParseByte(UITextBox field, byte defaultValue = 0)
        => byte.TryParse(field.Text, out var value) ? value : defaultValue;

    /// <summary>Sets the form to make a new group box, with the default values. The host shows it.</summary>
    public void ShowAsOwner()
    {
        ViewerSourceName = null;

        TitleField.Text = string.Empty;
        ExtraField.Text = string.Empty;
        MinLevelField.Text = "1";
        MaxLevelField.Text = "99";

        foreach (var field in WantedFields)
            field.Text = "0";

        SetMode(GroupRecruitMode.OwnerNew);
    }

    /// <summary>Sets the form to change your group box, with the values from the server. The host shows it.</summary>
    public void ShowAsOwnerEdit(GroupRecruitInfo info)
    {
        ViewerSourceName = null;
        Populate(info);
        SetMode(GroupRecruitMode.OwnerEdit);
    }

    /// <summary>
    ///     Sets the form to show the group box of <paramref name="sourceName" />, read-only. The fields are disabled, so
    ///     that they take no focus and the keys go on to the screen. The host shows it.
    /// </summary>
    public void ShowAsViewer(string sourceName, GroupRecruitInfo info)
    {
        ViewerSourceName = sourceName;
        Populate(info);
        SetMode(GroupRecruitMode.Viewer);
    }

    private void Populate(GroupRecruitInfo info)
    {
        TitleField.Text = info.GroupName;
        ExtraField.Text = info.Note;
        MinLevelField.Text = info.StartingLevel.ToString();
        MaxLevelField.Text = info.EndingLevel.ToString();

        byte[] current = [info.CurrentWarriors, info.CurrentWizards, info.CurrentRogues, info.CurrentPriests, info.CurrentMonks];
        byte[] wanted = [info.WarriorsWanted, info.WizardsWanted, info.RoguesWanted, info.PriestsWanted, info.MonksWanted];

        for (var i = 0; i < NUM_CLASSES; i++)
        {
            CurrentLabels[i].Text = current[i].ToString();
            WantedFields[i].Text = wanted[i].ToString();
        }

        TotalCurrentLabel.Text = current.Sum(count => count).ToString();
        TotalWantedLabel.Text = wanted.Sum(count => count).ToString();
    }

    //the viewer is read-only and shows the counts that the group has now; the owner modes edit the box
    private void SetMode(GroupRecruitMode mode)
    {
        var viewer = mode == GroupRecruitMode.Viewer;

        foreach (var field in AllFields)
            field.Enabled = !viewer;

        CurrentHeader.Visible = viewer;

        foreach (var label in CurrentLabels)
            label.Visible = viewer;

        TotalLabel.Visible = viewer;
        TotalWantedLabel.Visible = viewer;
        TotalCurrentLabel.Visible = viewer;

        if (Mode == mode)
            return;

        Mode = mode;
        ModeChanged?.Invoke();
    }
}
