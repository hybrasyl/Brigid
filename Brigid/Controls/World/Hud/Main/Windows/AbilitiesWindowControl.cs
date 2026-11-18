#region
using Brigid.Controls.Components;
using Brigid.Controls.Generic;
using Brigid.Data.Models;
using Brigid.Definitions;
using Brigid.Rendering;
using Brigid.Systems;
using Brigid.ViewModel;
using Microsoft.Xna.Framework;
#endregion

namespace Brigid.Controls.World.Hud.Main.Windows;

/// <summary>
///     The content of the Abilities window of the new HUD (BRIG-149, BRIG-127 D21, D35): the Abilities and Traits tabs and
///     the page arrows with the page number on the first line, the Unlearned and Learned filters on the second, and pages
///     of 16 rows of <see cref="AbilityList" />. The filters, the arrows and the page number belong to the Abilities tab;
///     the Traits tab is a placeholder (D6). The window lays out from the size of its content
///     (<see cref="MainHudLayout.AbilitiesIn" />). The list builds again at the next
///     update after a book slot changes, a filter changes, the metadata arrives, or the attributes change while the
///     Unlearned filter is on (the attributes decide the blue or grey icon of an ability that the player can learn); it
///     keeps its page.
/// </summary>
internal sealed class AbilitiesWindowControl : HudWindowContent
{
    private readonly SkillBook Skills;
    private readonly SpellBook Spells;
    private readonly PlayerAttributes Attributes;
    private readonly Func<AbilityMetadata?> MetadataSource;
    private readonly Func<AbilityMetadataEntry, AbilityIconState> StateOf;
    private readonly TabStrip Tabs;
    private readonly CheckBox LearnedBox;
    private readonly CheckBox UnlearnedBox;
    private readonly UILabel LearnedLabel;
    private readonly UILabel UnlearnedLabel;
    private readonly TextButton PreviousButton;
    private readonly TextButton NextButton;
    private readonly UILabel PageNumber;
    private readonly UILabel TraitsText;
    private readonly AbilityRowControl[] Rows = new AbilityRowControl[AbilityList.PAGE];
    private readonly ClockWedgeCache Wedges = new(MainHudLayout.ABILITY_ICON);
    private AbilityMetadata? Metadata;
    private bool Dirty = true;

    /// <param name="skills">The skill book.</param>
    /// <param name="spells">The spell book.</param>
    /// <param name="attributes">The player's attributes, which decide the state of an ability that the player can learn.</param>
    /// <param name="metadata">The class ability metadata, read at each update, so the list builds again when it arrives.</param>
    /// <param name="iconSource">The icon of a row; null for the icons of <see cref="UiRenderer" />.</param>
    /// <param name="stateOf">The state of an ability that the player can learn; null for <see cref="AbilityRequirements" />.</param>
    public AbilitiesWindowControl(
        SkillBook skills,
        SpellBook spells,
        PlayerAttributes attributes,
        Func<AbilityMetadata?> metadata,
        Func<AbilityRow, IconTexture?>? iconSource = null,
        Func<AbilityMetadataEntry, AbilityIconState>? stateOf = null)
    {
        Name = "abilities_window";
        Skills = skills;
        Spells = spells;
        Attributes = attributes;
        MetadataSource = metadata;
        StateOf = stateOf ?? AbilityRequirements.ResolveIconState;
        iconSource ??= IconOf;

        Tabs = new TabStrip(["Abilities", "Traits"], MainHudLayout.ABILITY_TAB_WIDTH, MainHudLayout.ABILITY_TAB_HEIGHT) { Name = "tabs" };
        Tabs.SetSelected(0);
        Tabs.TabClicked += SelectTab;
        AddChild(Tabs);

        (UnlearnedBox, UnlearnedLabel) = AddFilter("unlearned", "Unlearned", false);
        (LearnedBox, LearnedLabel) = AddFilter("learned", "Learned", true);
        PreviousButton = AddPageButton("previous_page", "<", -1);
        NextButton = AddPageButton("next_page", ">", 1);

        PageNumber = AddText("page_number", HorizontalAlignment.Center, ThemeRoles.TextDisabled);

        for (var i = 0; i < Rows.Length; i++)
        {
            Rows[i] = new AbilityRowControl(iconSource, CooldownOf, Wedges) { Name = $"row_{i}" };
            Rows[i].UseRequested += row => UseRequested?.Invoke(row);
            Rows[i].DetailsRequested += row => DetailsRequested?.Invoke(row);
            AddChild(Rows[i]);
        }

        TraitsText = AddText("traits", HorizontalAlignment.Center, ThemeRoles.TextDisabled);
        TraitsText.Text = "Traits come in a later update.";
        TraitsText.Visible = false;

        Skills.SlotChanged += OnBookChanged;
        Skills.Cleared += OnChanged;
        Spells.SlotChanged += OnBookChanged;
        Spells.Cleared += OnChanged;
        Attributes.Changed += OnAttributesChanged;
    }

    /// <summary>A left click on a learned ability: the world screen uses it as a click on its book slot.</summary>
    public event Action<AbilityRow>? UseRequested;

    /// <summary>
    ///     A right click on an ability: the world screen opens the castable popup of a learned ability, or the details of the
    ///     status book for one that the player can learn.
    /// </summary>
    public event Action<AbilityRow>? DetailsRequested;

    /// <summary>The rows of the list for the filters that are on.</summary>
    public IReadOnlyList<AbilityRow> Entries { get; private set; } = [];

    /// <summary>The page that shows, from 0.</summary>
    public int Page { get; private set; }

    /// <summary>The row controls of the page, in the order of <see cref="AbilityList.PlaceOf" />.</summary>
    internal IReadOnlyList<AbilityRowControl> RowControls => Rows;

    internal CheckBox LearnedFilter => LearnedBox;

    internal CheckBox UnlearnedFilter => UnlearnedBox;

    internal string PageText => PageNumber.Text;

    internal bool ShowsTraits => Tabs.SelectedIndex == 1;

    /// <summary>Shows another page; a page past the end stops at the end.</summary>
    public void TurnPage(int delta)
    {
        Page = AbilityList.ClampPage(Page + delta, Entries.Count);
        ShowPage();
    }

    /// <summary>Selects the Abilities (0) or the Traits (1) tab.</summary>
    internal void SelectTab(int index)
    {
        Tabs.SetSelected(index);
        ShowPage();
    }

    public override void Update(GameTime gameTime)
    {
        Refresh();

        base.Update(gameTime);
    }

    /// <summary>Builds the list again when a book, a filter, the attributes or the metadata changed since the last build.</summary>
    internal void Refresh()
    {
        var metadata = MetadataSource();

        if (!Dirty && ReferenceEquals(metadata, Metadata))
            return;

        Dirty = false;
        Metadata = metadata;
        Entries = AbilityList.Build(Skills, Spells, metadata, LearnedBox.Checked, UnlearnedBox.Checked, StateOf);
        Page = AbilityList.ClampPage(Page, Entries.Count);
        ShowPage();
    }

    public override void Dispose()
    {
        Skills.SlotChanged -= OnBookChanged;
        Skills.Cleared -= OnChanged;
        Spells.SlotChanged -= OnBookChanged;
        Spells.Cleared -= OnChanged;
        Attributes.Changed -= OnAttributesChanged;
        Wedges.Dispose();

        base.Dispose();
    }

    protected override void LayOut(int width, int height)
    {
        var layout = MainHudLayout.AbilitiesIn(width, height);

        Tabs.X = layout.Tabs.X;
        Tabs.Y = layout.Tabs.Y;
        Place(UnlearnedBox, layout.UnlearnedBox);
        UnlearnedLabel.SetBounds(layout.UnlearnedLabel);
        Place(LearnedBox, layout.LearnedBox);
        LearnedLabel.SetBounds(layout.LearnedLabel);
        Place(PreviousButton, layout.PreviousPage);
        Place(NextButton, layout.NextPage);
        PageNumber.SetBounds(layout.PageNumber);
        TraitsText.SetBounds(layout.Traits);

        for (var i = 0; i < Rows.Length; i++)
        {
            var (column, row) = AbilityList.PlaceOf(i);
            Rows[i].SetBounds(layout.RowAt(column, row));
        }
    }

    private void ShowPage()
    {
        var traits = ShowsTraits;
        TraitsText.Visible = traits;

        foreach (var element in new UIElement[] { UnlearnedBox, UnlearnedLabel, LearnedBox, LearnedLabel, PreviousButton, NextButton, PageNumber })
            element.Visible = !traits;

        PageNumber.Text = $"{Page + 1} / {AbilityList.PageCount(Entries.Count)}";

        for (var i = 0; i < Rows.Length; i++)
        {
            var index = Page * AbilityList.PAGE + i;
            Rows[i].Show(!traits && (index < Entries.Count) ? Entries[index] : null);
        }
    }

    //the part of the cooldown that is left: 1 just after a use, 0 when the ability is ready
    internal float CooldownOf(AbilityRow row)
        => row.Slot is not { } slot ? 0 : row.IsSpell ? Spells.GetCooldownPercent(slot) : Skills.GetCooldownPercent(slot);

    private void OnBookChanged(byte slot) => Dirty = true;

    private void OnChanged() => Dirty = true;

    //an attributes packet comes at each change of the vitals; only the unlearned rows read the attributes
    private void OnAttributesChanged()
    {
        if (UnlearnedBox.Checked)
            Dirty = true;
    }

    private (CheckBox Box, UILabel Label) AddFilter(string name, string text, bool on)
    {
        var check = new CheckBox
        {
            Name = $"{name}_filter",
            Checked = on
        };
        check.CheckedChanged += _ => Dirty = true;
        AddChild(check);

        var label = AddText($"{name}_label", HorizontalAlignment.Left, ThemeRoles.Text);
        label.Text = text;

        return (check, label);
    }

    private TextButton AddPageButton(string name, string text, int delta)
    {
        var button = new TextButton(text, MainHudLayout.ABILITY_ARROW, MainHudLayout.ABILITY_ARROW) { Name = name };
        button.Clicked += () => TurnPage(delta);
        AddChild(button);

        return button;
    }

    private UILabel AddText(string name, HorizontalAlignment alignment, string role)
    {
        var label = HudText.Label(name, role, alignment);
        AddChild(label);

        return label;
    }

    //a learned ability shows its book icon; one that the player can learn shows the blue or grey icon of the status book
    private static IconTexture? IconOf(AbilityRow row)
    {
        if (UiRenderer.Instance is not { } renderer)
            return null;

        if (!row.IsLearned && row.Entry is { } entry)
            return AbilityRequirements.ResolveIcon(entry, row.State);

        return row.IsSpell ? renderer.GetSpellIcon(row.IconSprite) : renderer.GetSkillIcon(row.IconSprite);
    }
}
