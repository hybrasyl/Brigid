#region
using Brigid.Controls.Components;
using DALib.Networking.Packets.Server;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
#endregion

namespace Brigid.Controls.World.Popups;

/// <summary>
///     Group recruitment configuration panel using _ngcdlg1 prefab. Three states:
///     Owner-New (create a group box), Owner-Edit (modify/delete existing), Viewer (view + request to join).
/// </summary>
public sealed class GroupRecruitPanel : PrefabPanel
{
    //class labels follow a 27px vertical spacing starting at y=206
    private const int CLASS_LABEL_START_Y = 206;
    private const int CLASS_ROW_SPACING = 27;
    private const int CLASS_O_X = 103;
    private const int CLASS_O_WIDTH = 18;
    private const int CLASS_O_HEIGHT = 12;
    private const int CLASS_W_X = 148;
    private const int CLASS_W_WIDTH = 18;
    private const int CLASS_W_HEIGHT = 16;
    private const int NUM_CLASSES = 5;

    //viewer mode labels
    private readonly UILabel?[] ClassCurrentLabels = new UILabel?[NUM_CLASSES];

    //owner mode fields
    private readonly UITextBox?[] ClassMaxFields = new UITextBox?[NUM_CLASSES];
    private readonly UILabel?[] ClassMaxLabels = new UILabel?[NUM_CLASSES];

    private string? ViewerSourceName;

    public UIButton? BeginButton { get; }
    public UIButton? CancelButton { get; }
    public UITextBox? ExtraField { get; }
    public UITextBox? MaxLevelField { get; }
    public UITextBox? MinLevelField { get; }
    public UIButton? ModifyButton { get; }
    public UIButton? QueryJoinButton { get; }
    public UIButton? ResetButton { get; }

    public UITextBox? TitleField { get; }
    public UILabel? TotalOnlineLabel { get; }
    public UILabel? TotalWantedLabel { get; }

    public GroupRecruitPanel(bool center = false)
        : base("_ngcdlg1", center)
    {
        Name = "GroupRecruit";
        Visible = false;
        UsesControlStack = true;

        //text fields from prefab
        TitleField = CreateTextBox("TITLE", 18);
        ExtraField = CreateTextBox("EXTRA", 60);
        MinLevelField = CreateTextBox("N_LEVEL_MIN", 2);
        MaxLevelField = CreateTextBox("N_LEVEL_MAX", 2);

        TitleField?.ForegroundColor = LegendColors.White;

        if (ExtraField is not null)
        {
            ExtraField.ForegroundColor = LegendColors.White;
            ExtraField.IsMultiLine = true;
            ExtraField.ClampToVisibleArea = true;
        }

        MinLevelField?.ForegroundColor = LegendColors.White;

        MaxLevelField?.ForegroundColor = LegendColors.White;

        //summary labels (viewer mode)
        TotalOnlineLabel = CreateLabel("N_TOTAL_O");
        TotalWantedLabel = CreateLabel("N_TOTAL_W");

        //class fields — try prefab first, then create manually for missing ones
        for (var i = 0; i < NUM_CLASSES; i++)
        {
            var onlineName = $"N_CLASS{i}_O";
            var wantedName = $"N_CLASS{i}_W";
            var rowY = CLASS_LABEL_START_Y + i * CLASS_ROW_SPACING;

            //owner mode: wanted (max) count as text box
            ClassMaxFields[i] = CreateTextBox(wantedName, 2);

            if (ClassMaxFields[i] is null)
            {
                ClassMaxFields[i] = new UITextBox
                {
                    Name = wantedName,
                    X = CLASS_W_X,
                    Y = rowY - 1,
                    Width = CLASS_W_WIDTH,
                    Height = CLASS_W_HEIGHT,
                    MaxLength = 3,
                    ForegroundColor = LegendColors.White
                };

                AddChild(ClassMaxFields[i]!);
            } else
                ClassMaxFields[i]!.ForegroundColor = LegendColors.White;

            //viewer mode: current count label
            ClassCurrentLabels[i] = CreateLabel(onlineName);

            if (ClassCurrentLabels[i] is null)
            {
                ClassCurrentLabels[i] = new UILabel
                {
                    Name = onlineName,
                    X = CLASS_O_X,
                    Y = rowY,
                    Width = CLASS_O_WIDTH,
                    Height = CLASS_O_HEIGHT,
                    ForegroundColor = LegendColors.White
                };

                AddChild(ClassCurrentLabels[i]!);
            }

            //viewer mode: max count label (overlays the text box position)
            ClassMaxLabels[i] = new UILabel
            {
                Name = $"CLASS{i}_MAX_LABEL",
                X = CLASS_W_X,
                Y = rowY,
                Width = CLASS_W_WIDTH,
                Height = CLASS_O_HEIGHT,
                ForegroundColor = LegendColors.White,
                Visible = false
            };

            AddChild(ClassMaxLabels[i]!);
        }

        //buttons
        BeginButton = CreateButton("BTN_BEGIN");
        ModifyButton = CreateButton("BTN_MODIFY");
        ResetButton = CreateButton("BTN_RESET");
        CancelButton = CreateButton("BTN_CANCEL");
        QueryJoinButton = CreateButton("BTN_QUERY_JOIN");

        //default to owner-new state: only begin + cancel visible
        ModifyButton?.Visible = false;

        ResetButton?.Visible = false;

        QueryJoinButton?.Visible = false;

        //button events
        if (BeginButton is not null)
            BeginButton.Clicked += HandleBegin;

        if (ModifyButton is not null)
            ModifyButton.Clicked += HandleModify;

        if (ResetButton is not null)
            ResetButton.Clicked += HandleReset;

        if (CancelButton is not null)
            CancelButton.Clicked += HandleCancel;

        if (QueryJoinButton is not null)
            QueryJoinButton.Clicked += HandleQueryJoin;
    }

    public override void Draw(SpriteBatch spriteBatch)
    {
        if (!Visible)
            return;

        base.Draw(spriteBatch);
    }

    /// <summary>
    ///     Cancel just closes — does NOT delete the group box.
    /// </summary>
    private void HandleCancel()
    {
        Hide();
        OnClose?.Invoke();
    }

    private void HandleBegin()
    {
        SendCreateOrModify();
        Hide();
        OnClose?.Invoke();
    }

    private void HandleModify()
    {
        SendCreateOrModify();
        // Retail behavior: dialog stays open after MODIFY so the owner sees
        // the server-authoritative values echo back via ShowGroupBox(self).
        // Ref: docs/research/group-ui-original-re.md §2.2.
    }

    private void SendCreateOrModify()
    {
        var name = TitleField?.Text ?? string.Empty;
        var note = ExtraField?.Text ?? string.Empty;

        OnCreateGroupBox?.Invoke(
            name,
            note,
            ParseByte(MinLevelField, 1),
            ParseByte(MaxLevelField, 99),
            ParseByte(ClassMaxFields[0]),
            ParseByte(ClassMaxFields[1]),
            ParseByte(ClassMaxFields[2]),
            ParseByte(ClassMaxFields[3]),
            ParseByte(ClassMaxFields[4]));
    }

    private void HandleQueryJoin()
    {
        if (ViewerSourceName is not null)
            OnRequestJoin?.Invoke(ViewerSourceName);

        Hide();
        OnClose?.Invoke();
    }

    /// <summary>
    ///     Reset deletes the group box on the server, then closes.
    /// </summary>
    private void HandleReset()
    {
        OnRemoveGroupBox?.Invoke();
        Hide();
        OnClose?.Invoke();
    }

    public event CloseHandler? OnClose;
    public event CreateGroupBoxHandler? OnCreateGroupBox;
    public event RemoveGroupBoxHandler? OnRemoveGroupBox;
    public event RequestJoinHandler? OnRequestJoin;

    private static byte ParseByte(UITextBox? field, byte defaultValue = 0)
    {
        if (field is null || string.IsNullOrEmpty(field.Text))
            return defaultValue;

        return byte.TryParse(field.Text, out var value) ? value : defaultValue;
    }

    private void PopulateFromGroupBoxInfo(GroupRecruitInfo info)
    {
        TitleField?.Text = info.GroupName;

        ExtraField?.Text = info.Note;

        MinLevelField?.Text = info.StartingLevel.ToString();

        MaxLevelField?.Text = info.EndingLevel.ToString();

        byte[] currentCounts =
        [
            info.CurrentWarriors,
            info.CurrentWizards,
            info.CurrentRogues,
            info.CurrentPriests,
            info.CurrentMonks
        ];

        byte[] maxCounts =
        [
            info.WarriorsWanted,
            info.WizardsWanted,
            info.RoguesWanted,
            info.PriestsWanted,
            info.MonksWanted
        ];

        var totalCurrent = 0;
        var totalMax = 0;

        for (var i = 0; i < NUM_CLASSES; i++)
        {
            if (ClassCurrentLabels[i] is not null)
                ClassCurrentLabels[i]!.Text = currentCounts[i]
                    .ToString();

            if (ClassMaxLabels[i] is not null)
                ClassMaxLabels[i]!.Text = maxCounts[i]
                    .ToString();

            if (ClassMaxFields[i] is not null)
                ClassMaxFields[i]!.Text = maxCounts[i]
                    .ToString();

            totalCurrent += currentCounts[i];
            totalMax += maxCounts[i];
        }

        TotalOnlineLabel?.Text = totalCurrent.ToString();

        TotalWantedLabel?.Text = totalMax.ToString();
    }

    private void ResetFields()
    {
        TitleField?.Text = string.Empty;

        ExtraField?.Text = string.Empty;

        MinLevelField?.Text = "1";

        MaxLevelField?.Text = "99";

        for (var i = 0; i < NUM_CLASSES; i++)
            if (ClassMaxFields[i] is not null)
                ClassMaxFields[i]!.Text = "0";
    }

    /// <summary>
    ///     Owner-Edit: Modify + Reset + Cancel. Has existing group box.
    /// </summary>
    private void SetOwnerEditMode()
    {
        SetOwnerFields(true);

        BeginButton?.Visible = false;

        ModifyButton?.Visible = true;

        ResetButton?.Visible = true;

        CancelButton?.Visible = true;

        QueryJoinButton?.Visible = false;
    }

    private void SetOwnerFields(bool enabled)
    {
        TitleField?.Enabled = enabled;

        ExtraField?.Enabled = enabled;

        MinLevelField?.Enabled = enabled;

        MaxLevelField?.Enabled = enabled;

        for (var i = 0; i < NUM_CLASSES; i++)
        {
            if (ClassMaxFields[i] is not null)
            {
                ClassMaxFields[i]!.Visible = enabled;
                ClassMaxFields[i]!.Enabled = enabled;
            }

            if (ClassCurrentLabels[i] is not null)
                ClassCurrentLabels[i]!.Visible = !enabled;

            if (ClassMaxLabels[i] is not null)
                ClassMaxLabels[i]!.Visible = !enabled;
        }

        TotalOnlineLabel?.Visible = !enabled;

        TotalWantedLabel?.Visible = !enabled;
    }

    /// <summary>
    ///     Owner-New: Begin + Cancel. No existing group box.
    /// </summary>
    private void SetOwnerNewMode()
    {
        SetOwnerFields(true);

        BeginButton?.Visible = true;

        ModifyButton?.Visible = false;

        ResetButton?.Visible = false;

        CancelButton?.Visible = true;

        QueryJoinButton?.Visible = false;
    }

    /// <summary>
    ///     Viewer: QueryJoin + Cancel. Read-only fields.
    /// </summary>
    private void SetViewerMode()
    {
        SetOwnerFields(false);

        BeginButton?.Visible = false;

        ModifyButton?.Visible = false;

        ResetButton?.Visible = false;

        CancelButton?.Visible = true;

        QueryJoinButton?.Visible = true;
    }

    /// <summary>
    ///     Shows the panel in owner mode for creating a new group box.
    /// </summary>
    public void ShowAsOwner()
    {
        ViewerSourceName = null;

        SetOwnerNewMode();
        ResetFields();
        Show();
    }

    /// <summary>
    ///     Shows the panel in owner mode for editing an existing group box.
    /// </summary>
    public void ShowAsOwnerEdit(GroupRecruitInfo info)
    {
        ViewerSourceName = null;

        SetOwnerEditMode();
        PopulateFromGroupBoxInfo(info);
        Show();
    }

    /// <summary>
    ///     Shows the panel in viewer mode populated from server GroupBox data.
    /// </summary>
    public void ShowAsViewer(string sourceName, GroupRecruitInfo info)
    {
        ViewerSourceName = sourceName;

        SetViewerMode();
        PopulateFromGroupBoxInfo(info);
        Show();
    }

    public override void OnKeyDown(KeyDownEvent e)
    {
        if (e.Key == Keys.Escape)
        {
            Hide();
            OnClose?.Invoke();
            e.Handled = true;
        }
    }
}