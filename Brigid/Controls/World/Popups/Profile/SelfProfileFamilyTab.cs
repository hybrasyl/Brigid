#region
using Brigid.Collections;
using Brigid.Controls.Components;
using Brigid.Data.Models;
#endregion

namespace Brigid.Controls.World.Popups.Profile;

/// <summary>
///     Family tab page (_nui_fm). Displays player and spouse name, plus 10 editable family member text fields.
/// </summary>
public sealed class SelfProfileFamilyTab : PrefabPanel
{
    private readonly UILabel? FamilyLabel;
    private readonly UILabel? SelfLabel;
    private readonly UITextBox?[] TextFields = new UITextBox?[10];

    public SelfProfileFamilyTab(string prefabName)
        : base(prefabName, false)
    {
        Name = prefabName;
        Visible = false;

        SelfLabel = CreateLabel("Self");
        SelfLabel?.Text = WorldState.PlayerName;
        SelfLabel?.HorizontalAlignment = HorizontalAlignment.Center;

        FamilyLabel = CreateLabel("Family");

        for (var i = 0; i < 10; i++)
        {
            TextFields[i] = CreateTextBox($"Text{i}");

            if (TextFields[i] is not null)
                TextFields[i]!.IsTabStop = true;
        }
    }

    public FamilyList GetFamilyMembers()
        => new()
        {
            Mother = TextFields[0]?.Text ?? string.Empty,
            Father = TextFields[1]?.Text ?? string.Empty,
            Son1 = TextFields[2]?.Text ?? string.Empty,
            Son2 = TextFields[3]?.Text ?? string.Empty,
            Brother1 = TextFields[4]?.Text ?? string.Empty,
            Brother2 = TextFields[5]?.Text ?? string.Empty,
            Brother3 = TextFields[6]?.Text ?? string.Empty,
            Brother4 = TextFields[7]?.Text ?? string.Empty,
            Brother5 = TextFields[8]?.Text ?? string.Empty,
            Brother6 = TextFields[9]?.Text ?? string.Empty
        };

    /// <summary>
    ///     Updates the spouse name and refreshes the player name from WorldState.
    /// </summary>
    public void SetFamilyInfo(string spouseName)
    {
        SelfLabel?.Text = WorldState.PlayerName;
        FamilyLabel?.Text = spouseName;
    }

    /// <summary>
    ///     Sets a specific family stat text field (0-9).
    /// </summary>
    public void SetTextField(int index, string text)
    {
        if (index is >= 0 and < 10 && TextFields[index] is { } field)
            field.Text = text;
    }
}