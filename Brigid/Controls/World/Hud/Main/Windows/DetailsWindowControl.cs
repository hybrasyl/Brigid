#region
using System.Globalization;
using Brigid.Controls.Components;
using Brigid.Controls.Generic;
using Brigid.Controls.World.Popups.Profile;
using Brigid.Rendering;
using Brigid.ViewModel;
using Chaos.DarkAges.Definitions;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Brigid.Controls.World.Hud.Main.Windows;

/// <summary>
///     The content of the Details window of the new HUD (BRIG-151, BRIG-127 D34). It shows the facts of the player: Name,
///     Class, Species, Guild, Rank, Nation, and Title as a drop-down. It also shows the portrait, the profile text, the stat
///     points with Confirm, and the Activity Log of the session. It reads <see cref="SelfProfile" />,
///     <see cref="PlayerAttributes" /> and <see cref="ActivityLog" />. The server sends no list of titles yet (HS-1724), so
///     the drop-down holds the current title only and is disabled. A click on the profile text raises
///     <see cref="ProfileTextRequested" />. The stat points stay pending until Confirm (<see cref="StatRaise" />), and a
///     close of the window clears them. The window lays out from the size of its content
///     (<see cref="MainHudLayout.DetailsIn" />).
/// </summary>
internal sealed class DetailsWindowControl : HudWindowContent
{
    private static readonly string[] FieldNames = ["Name", "Class", "Species", "Guild", "Rank", "Nation", "Title"];

    private readonly SelfProfile Profile;
    private readonly PlayerAttributes Attributes;
    private readonly ActivityLog Activity;
    private readonly UILabel[] FieldLabels = new UILabel[MainHudLayout.DETAILS_FIELDS];
    private readonly UILabel[] FieldValues = new UILabel[MainHudLayout.DETAILS_FIELDS - 1];
    private readonly UIImage Portrait;
    private readonly ProfileTextBox ProfileText;
    private readonly UILabel[] StatLabels = new UILabel[StatRaise.Stats.Length];
    private readonly UILabel[] StatValues = new UILabel[StatRaise.Stats.Length];
    private readonly IconButton[] RaiseButtons = new IconButton[StatRaise.Stats.Length];
    private readonly IconButton[] LowerButtons = new IconButton[StatRaise.Stats.Length];
    private readonly UILabel RemainingText;
    private readonly List<UILabel> ActivityLines = [];

    //the full text of each activity line that is cut to its width, else null; the tooltip shows it
    private readonly List<string?> ActivityFullTexts = [];
    private readonly TooltipFrame ActivityTooltip = new(MainHudLayout.TOOLTIP_TEXT_WIDTH) { Name = "activity_tooltip" };
    private readonly FontGenerationStamp ActivityFit = new();
    private string? ActivityTooltipText;
    private byte[]? ShownPortrait;
    private DetailsLayout? Layout;
    private bool ShownWaiting;

    /// <param name="profile">The player's profile.</param>
    /// <param name="attributes">The player's attributes, which hold the stats and the unspent points.</param>
    /// <param name="activity">The activity log of the session.</param>
    /// <param name="clock">The time in milliseconds for the stat points; null for <see cref="Environment.TickCount64" />.</param>
    public DetailsWindowControl(SelfProfile profile, PlayerAttributes attributes, ActivityLog activity, Func<long>? clock = null)
    {
        Name = "details_window";
        Profile = profile;
        Attributes = attributes;
        Activity = activity;
        StatPoints = new StatRaise(clock);

        for (var i = 0; i < FieldLabels.Length; i++)
        {
            FieldLabels[i] = HudText.Label($"{FieldNames[i].ToLowerInvariant()}_label", ThemeRoles.TextDisabled);
            FieldLabels[i].Text = FieldNames[i];
            AddChild(FieldLabels[i]);
        }

        for (var i = 0; i < FieldValues.Length; i++)
        {
            FieldValues[i] = HudText.Label(FieldNames[i].ToLowerInvariant(), ThemeRoles.Text);
            AddChild(FieldValues[i]);
        }

        Portrait = new UIImage
        {
            Name = "portrait",
            ScaleToFit = true,
            IsHitTestVisible = false
        };
        AddChild(Portrait);

        ProfileText = new ProfileTextBox();
        ProfileText.Clicked += () => ProfileTextRequested?.Invoke();
        AddChild(ProfileText);

        for (var i = 0; i < StatRaise.Stats.Length; i++)
        {
            var stat = StatRaise.Stats[i];
            var name = stat.ToString().ToLowerInvariant();

            StatLabels[i] = HudText.Label($"{name}_label", ThemeRoles.TextDisabled);
            StatLabels[i].Text = stat.ToString();
            AddChild(StatLabels[i]);

            StatValues[i] = HudText.Label(name, ThemeRoles.Text);
            AddChild(StatValues[i]);

            RaiseButtons[i] = new IconButton(ThemeRoles.IconDetailsRaise, Rectangle.Empty) { Name = $"raise_{name}" };
            RaiseButtons[i].Clicked += () => Change(StatPoints.Add(stat));
            AddChild(RaiseButtons[i]);

            LowerButtons[i] = new IconButton(ThemeRoles.IconDetailsLower, Rectangle.Empty) { Name = $"lower_{name}" };
            LowerButtons[i].Clicked += () => Change(StatPoints.Remove(stat));
            AddChild(LowerButtons[i]);
        }

        RemainingText = HudText.Label("remaining", ThemeRoles.Text);
        AddChild(RemainingText);

        ConfirmButton = new TextButton("Confirm", MainHudLayout.DETAILS_CONFIRM_WIDTH, MainHudLayout.DETAILS_CONFIRM_HEIGHT) { Name = "confirm" };
        ConfirmButton.Clicked += Confirm;
        AddChild(ConfirmButton);

        TitleBox = NewTitleBox();

        ShowProfile();
        ShowAttributes();
        ShowActivity();

        Profile.Changed += ShowProfile;
        Attributes.Changed += ShowAttributes;
        Activity.Changed += ShowActivity;
    }

    /// <summary>Confirm: the world screen sends one point for each stat, in the order of the rows.</summary>
    public event Action<Stat>? RaiseRequested;

    /// <summary>A click on the profile text: the world screen opens the profile text editor.</summary>
    public event Action? ProfileTextRequested;

    internal StatRaise StatPoints { get; }

    internal TextButton ConfirmButton { get; }

    internal DropDown TitleBox { get; private set; }

    internal IReadOnlyList<IconButton> RaiseControls => RaiseButtons;

    internal IReadOnlyList<IconButton> LowerControls => LowerButtons;

    internal string FieldShown(int index) => FieldValues[index].Text;

    internal string StatShown(int index) => StatValues[index].Text;

    internal string RemainingShown => RemainingText.Text;

    internal string ProfileTextShown => ProfileText.Text;

    internal IReadOnlyList<string> ActivityShown => ActivityLines.Where(line => line.Visible).Select(line => line.Text).ToArray();

    internal Texture2D? PortraitTexture => Portrait.Texture;

    /// <summary>Clears the pending stat points, with nothing sent: the window closed.</summary>
    public void ClearPending()
    {
        StatPoints.Reset();
        ShowPoints();
    }

    //the points of a Confirm stop waiting when the server answers or the wait ends; the count shows the end of the wait
    public override void Update(GameTime gameTime)
    {
        if (ShownWaiting && (StatPoints.Waiting == 0))
            ShowPoints();

        //a new face or window size moves the width of the text, so the cut lines are fitted again
        if (Visible && ActivityFit.Changed)
            ShowActivity();

        base.Update(gameTime);
    }

    /// <summary>The full text of the activity line at <paramref name="index" /> when it is cut to its width, else null.</summary>
    internal string? ActivityFullTextAt(int index) => index < ActivityFullTexts.Count ? ActivityFullTexts[index] : null;

    /// <summary>The text that the activity line at <paramref name="index" /> shows.</summary>
    internal string ActivityTextAt(int index) => ActivityLines[index].Text;

    public override void Draw(SpriteBatch spriteBatch)
    {
        if (!Visible)
            return;

        base.Draw(spriteBatch);

        //a line cut with an ellipsis shows its full text at the cursor. The tooltip has no parent: its position is in
        //screen coordinates, and the window does not clip it
        var hovered = ActivityLines.FindIndex(line => line.Visible && line.ContainsPoint(InputBuffer.MouseX, InputBuffer.MouseY));

        if ((hovered < 0) || (ActivityFullTextAt(hovered) is not { } full))
            return;

        if (full != ActivityTooltipText)
        {
            ActivityTooltip.SetLines([TooltipLine.Themed(full, ThemeRoles.Text)]);
            ActivityTooltipText = full;
        }

        ActivityTooltip.PlaceAtCursor(InputBuffer.MouseX, InputBuffer.MouseY);
        ActivityTooltip.Visible = true;
        ActivityTooltip.Draw(spriteBatch);
    }

    public override void Dispose()
    {
        Profile.Changed -= ShowProfile;
        Attributes.Changed -= ShowAttributes;
        Activity.Changed -= ShowActivity;
        ActivityTooltip.Dispose();

        base.Dispose();
    }

    protected override void LayOut(int width, int height)
    {
        var layout = MainHudLayout.DetailsIn(width, height);
        Layout = layout;

        for (var i = 0; i < FieldLabels.Length; i++)
            FieldLabels[i].SetBounds(layout.Labels[i]);

        for (var i = 0; i < FieldValues.Length; i++)
            FieldValues[i].SetBounds(layout.Values[i]);

        PlaceTitleBox();
        Portrait.SetBounds(layout.Portrait);
        ProfileText.SetBounds(layout.ProfileText);

        for (var i = 0; i < StatRaise.Stats.Length; i++)
        {
            StatLabels[i].SetBounds(layout.StatLabel(i));
            StatValues[i].SetBounds(layout.StatValue(i));
            RaiseButtons[i].SetBounds(layout.Raise(i));
            LowerButtons[i].SetBounds(layout.Lower(i));
        }

        RemainingText.SetBounds(layout.Remaining);
        Place(ConfirmButton, layout.Confirm);
        LayOutActivity(layout);
    }

    //the boxes of the portrait, the stat points and the Activity Log: the hud.box part, else a dark box with a border
    protected override void DrawChrome(SpriteBatch spriteBatch, Rectangle bounds)
    {
        if (Layout is not { } layout)
            return;

        DrawBox(spriteBatch, bounds, layout.Portrait);
        DrawBox(spriteBatch, bounds, layout.Stats);
        DrawBox(spriteBatch, bounds, layout.Activity);
    }

    private void DrawBox(SpriteBatch spriteBatch, Rectangle bounds, Rectangle box)
    {
        var screen = new Rectangle(bounds.X + box.X, bounds.Y + box.Y, box.Width, box.Height);

        if (TryDrawThemePart(spriteBatch, ThemeRoles.HudBox, screen))
            return;

        DrawRectClipped(spriteBatch, screen, DialogPalette.SlotFill);
        DrawBorderClipped(spriteBatch, screen, ThemeColor(ThemeRoles.Divider));
    }

    private void ShowProfile()
    {
        FieldValues[0].Text = Profile.Name;
        FieldValues[1].Text = Profile.ClassName;
        FieldValues[2].Text = Profile.Species;
        FieldValues[3].Text = Profile.GuildName;
        FieldValues[4].Text = Profile.GuildRank;
        FieldValues[5].Text = Profile.NationName;

        if (!TitleBox.Entries.SequenceEqual(Profile.Titles))
        {
            //the drop-down has a fixed list, so new titles make a new one; RemoveChild disposes the old one
            RemoveChild(TitleBox.Name);
            TitleBox = NewTitleBox();
            PlaceTitleBox();
        }

        if (!ReferenceEquals(ShownPortrait, Profile.Portrait))
        {
            ShownPortrait = Profile.Portrait;
            ProfilePortrait.Show(Portrait, Profile.Portrait);
        }

        ProfileText.Text = Profile.ProfileText;
    }

    //the server sends no list of titles yet (HS-1724): the drop-down is disabled, with the current title selected
    private DropDown NewTitleBox()
    {
        //PlaceTitleBox gives the box the width of the value column
        var box = new DropDown(Profile.Titles, 0, MainHudLayout.DETAILS_ROW)
        {
            Name = "title",
            Enabled = false
        };
        box.SetSelected(Math.Max(0, Profile.Titles.ToList().IndexOf(Profile.Title)));
        AddChild(box);

        return box;
    }

    private void PlaceTitleBox()
    {
        if (Layout is { } layout)
            TitleBox.SetBounds(layout.Values[MainHudLayout.DETAILS_FIELDS - 1]);
    }

    private void ShowAttributes()
    {
        StatPoints.SetUnspent(Attributes.UnspentPoints);
        ShowPoints();
    }

    private void Change(bool changed)
    {
        if (changed)
            ShowPoints();
    }

    private void ShowPoints()
    {
        byte[] values = [Attributes.Str, Attributes.Int, Attributes.Wis, Attributes.Con, Attributes.Dex];

        for (var i = 0; i < StatRaise.Stats.Length; i++)
        {
            var pending = StatPoints.PendingOf(StatRaise.Stats[i]);
            StatValues[i].Text = pending > 0
                ? FormattableString.Invariant($"{values[i]} +{pending}")
                : values[i].ToString(CultureInfo.InvariantCulture);
            RaiseButtons[i].Enabled = StatPoints.Remaining > 0;
            LowerButtons[i].Enabled = pending > 0;
        }

        RemainingText.Text = FormattableString.Invariant($"Points {StatPoints.Remaining}");
        ConfirmButton.SetEnabled(StatPoints.HasPending);
        ShownWaiting = StatPoints.Waiting > 0;
    }

    private void Confirm()
    {
        foreach (var stat in StatPoints.Confirm())
            RaiseRequested?.Invoke(stat);

        ShowPoints();
    }

    private void LayOutActivity(DetailsLayout layout)
    {
        while (ActivityLines.Count < layout.ActivityLineCount)
        {
            //the line is fitted with an ellipsis, so it never squeezes: its shadow margin would start the squeeze
            var line = HudText.Label($"activity_{ActivityLines.Count}", ThemeRoles.Text);
            line.ShrinkToFit = false;
            ActivityLines.Add(line);
            AddChild(line);
        }

        for (var i = 0; i < ActivityLines.Count; i++)
            ActivityLines[i].SetBounds(layout.ActivityLine(i));

        ShowActivity();
    }

    //a line that is wider than its label ends in an ellipsis, and its tooltip shows the full text: a squeeze to fit made
    //the glyphs of a long line overlap (Sabrael, 2026-11-19)
    private void ShowActivity()
    {
        ActivityFit.Mark();

        while (ActivityFullTexts.Count < ActivityLines.Count)
            ActivityFullTexts.Add(null);

        for (var i = 0; i < ActivityLines.Count; i++)
        {
            var line = ActivityLines[i];
            var shown = i < Activity.Entries.Count;
            var full = shown
                ? string.Create(CultureInfo.InvariantCulture, $"{Activity.Entries[i].Time:HH:mm} {Activity.Entries[i].Text}")
                : string.Empty;
            var fitted = TextRenderer.FitWithEllipsis(full, line.Width, line.MeasureText);

            line.Visible = shown;
            line.Text = fitted;
            ActivityFullTexts[i] = fitted == full ? null : full;
        }
    }
}
