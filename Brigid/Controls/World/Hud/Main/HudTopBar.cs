#region
using System.Globalization;
using Brigid.Controls.Components;
using Brigid.Controls.Generic;
using Brigid.Systems;
using Microsoft.Xna.Framework;
#endregion

namespace Brigid.Controls.World.Hud.Main;

/// <summary>
///     The top bar of the new HUD (BRIG-127 D8, D15, D17, D39): Server (the latency icon, the server name and the TLS
///     icon), Gold, the zone name with the player's tile, the mail and parcel icons, Weight, and Clock. A hover on a box
///     shows its tooltip under it. The positions are in <see cref="MainHudLayout" />.
/// </summary>
internal sealed class HudTopBar : HudBarPanel
{
    private readonly TimeProvider Clock;
    private readonly HudTooltip Tooltip;

    private readonly RoleIcon LatencyIcon;
    private readonly UILabel ServerName;
    private readonly RoleIcon SecureIcon;
    private readonly UILabel GoldLabel;
    private readonly UILabel ZoneLabel;
    private readonly RoleIcon MailIcon;
    private readonly RoleIcon ParcelIcon;
    private readonly UILabel WeightLabel;
    private readonly UILabel ClockLabel;

    private long? LatencyMs;
    private uint Gold;
    private int CurrentWeight;
    private int MaxWeight;
    private string Zone = string.Empty;
    private int TileX;
    private int TileY;

    //the time, in UTC, at which the minute that the clock shows ends: the update reads the cheap UTC time each frame and
    //converts to the local time only at a new minute
    private DateTimeOffset ShownMinuteEnd;
    private bool ShowsGameTime;

    /// <param name="clock">The source of the local time; the tests give a fixed one.</param>
    /// <param name="tooltip">The tooltip that a hover on a box shows; the HUD owns it, so it can draw below the bar.</param>
    public HudTopBar(TimeProvider clock, HudTooltip tooltip)
        : base(ThemeRoles.HudTopBar, MainHudLayout.TopBar)
    {
        Name = "top_bar";
        Clock = clock;
        Tooltip = tooltip;

        var server = AddBox("server_box", MainHudLayout.ServerBox, ServerLines);
        LatencyIcon = AddIcon(server, "latency", MainHudLayout.BoxIconAt(0));
        SecureIcon = AddIcon(server, "secure", MainHudLayout.SecureIconIn(server.Bounds));
        SecureIcon.Role = ThemeRoles.IconHudSecure;
        SecureIcon.Visible = false;
        ServerName = server.AddLabel("server_name", MainHudLayout.ServerNameIn(server.Bounds), HorizontalAlignment.Left);
        server.Clicked += () => ServerClicked?.Invoke();

        var gold = AddBox("gold_box", MainHudLayout.GoldBox, () => [Line(HudFormat.GoldTooltip(Gold) ?? "Gold")]);
        AddIcon(gold, "gold_icon", MainHudLayout.BoxIconAt(0)).Role = ThemeRoles.IconHudGold;
        GoldLabel = gold.AddLabel("gold", MainHudLayout.BoxTextIn(gold.Bounds), HorizontalAlignment.Left);

        var zone = AddBox("zone_box", MainHudLayout.ZoneBox, () => [Line(ZoneLabel!.Text)]);
        ZoneLabel = zone.AddLabel("zone", MainHudLayout.BoxTextIn(zone.Bounds, withIcon: false), HorizontalAlignment.Center);

        var mail = AddBox("mail_box", MainHudLayout.MailBox, MailLines);
        MailIcon = AddIcon(mail, "mail", MainHudLayout.BoxIconAt(0));
        MailIcon.Role = ThemeRoles.IconHudMail;
        ParcelIcon = AddIcon(mail, "parcel", MainHudLayout.BoxIconAt(1));
        ParcelIcon.Role = ThemeRoles.IconHudParcel;
        mail.Clicked += () => MailClicked?.Invoke();
        SetMail(false, false);

        var weight = AddBox("weight_box", MainHudLayout.WeightBox, () => [Line($"Weight {CurrentWeight} of {MaxWeight}")]);
        AddIcon(weight, "weight_icon", MainHudLayout.BoxIconAt(0)).Role = ThemeRoles.IconHudWeight;
        WeightLabel = weight.AddLabel("weight", MainHudLayout.BoxTextIn(weight.Bounds), HorizontalAlignment.Left);

        var clockBox = AddBox("clock_box", MainHudLayout.ClockBox, ClockLines);
        ClockLabel = clockBox.AddLabel("clock", MainHudLayout.BoxTextIn(clockBox.Bounds, withIcon: false), HorizontalAlignment.Center);
        clockBox.Clicked += ToggleClock;

        ShowLatency(null);
        SetGold(0);
        SetWeight(0, 0);
        ShowClock();
    }

    /// <summary>The game time to show after a click on the clock, or null while no source gives one (D39).</summary>
    public Func<string?> GameTimeSource { get; set; } = static () => null;

    /// <summary>A click on the server box: the connection details.</summary>
    public event Action? ServerClicked;

    /// <summary>A click on the mail box: the mail.</summary>
    public event Action? MailClicked;

    public void SetServerName(string name) => ServerName.Text = name;

    public void SetTransportSecure(bool secure) => SecureIcon.Visible = secure;

    public void SetGold(uint gold)
    {
        Gold = gold;
        GoldLabel.Text = HudFormat.Gold(gold);
    }

    public void SetWeight(int current, int max)
    {
        (CurrentWeight, MaxWeight) = (current, max);
        WeightLabel.Text = HudFormat.Weight(current, max);
    }

    public void SetZoneName(string zone)
    {
        Zone = zone;
        ZoneLabel.Text = HudFormat.Zone(Zone, TileX, TileY);
    }

    public void SetCoords(int x, int y)
    {
        (TileX, TileY) = (x, y);
        ZoneLabel.Text = HudFormat.Zone(Zone, TileX, TileY);
    }

    /// <summary>Shows the mail icon while a letter is unread and the parcel icon while a parcel waits (D8: no flash).</summary>
    public void SetMail(bool mail, bool parcel)
    {
        MailIcon.Visible = mail;
        ParcelIcon.Visible = parcel;
    }

    /// <summary>Shows the bucket of <paramref name="ms" /> on the latency icon.</summary>
    public void ShowLatency(long? ms)
    {
        LatencyMs = ms;
        LatencyIcon.Role = LatencyMonitor.QualityOf(ms) switch
        {
            LatencyQuality.Good => ThemeRoles.IconHudLatencyGood,
            LatencyQuality.Fair => ThemeRoles.IconHudLatencyFair,
            LatencyQuality.Poor => ThemeRoles.IconHudLatencyPoor,
            _                   => ThemeRoles.IconHudLatencyNone
        };
    }

    public override void Update(GameTime gameTime)
    {
        //the monitor can change on another thread, so the bar reads it here, on the game thread, and once
        var latency = LatencyMonitor.LatencyMs;

        if (latency != LatencyMs)
            ShowLatency(latency);

        //the clock changes once each minute; a clock that goes back before the shown minute shows the new time too
        if (!ShowsGameTime)
        {
            var utc = Clock.GetUtcNow();

            if ((utc >= ShownMinuteEnd) || (utc < ShownMinuteEnd.AddMinutes(-1)))
                ShowClock();
        }

        base.Update(gameTime);
    }

    private void ShowClock()
    {
        var utc = Clock.GetUtcNow();
        var now = TimeZoneInfo.ConvertTime(utc, Clock.LocalTimeZone).DateTime;
        ShownMinuteEnd = new DateTimeOffset(utc.UtcTicks - utc.UtcTicks % TimeSpan.TicksPerMinute, TimeSpan.Zero).AddMinutes(1);
        ClockLabel.Text = HudFormat.Clock(now, CultureInfo.CurrentCulture);
    }

    //a click changes to the game time and back; with no game time it does nothing
    private void ToggleClock()
    {
        if (ShowsGameTime)
        {
            ShowsGameTime = false;
            ShowClock();

            return;
        }

        if (GameTimeSource() is not { } gameTime)
            return;

        ShowsGameTime = true;
        ClockLabel.Text = gameTime;
    }

    private IReadOnlyList<TooltipLine> ServerLines()
    {
        List<TooltipLine> lines =
        [
            Line(ServerName.Text),
            Line(LatencyMs is { } ms ? $"Latency {ms} ms" : "Latency: no reading")
        ];

        if (SecureIcon.Visible)
            lines.Add(Line("Secure connection (TLS)"));

        lines.Add(TooltipLine.Themed("Click for the connection details", ThemeRoles.TextDisabled));

        return lines;
    }

    private IReadOnlyList<TooltipLine> MailLines()
    {
        List<TooltipLine> lines = [];

        if (MailIcon.Visible)
            lines.Add(Line("Unread mail"));

        if (ParcelIcon.Visible)
            lines.Add(Line("A parcel waits at a post office"));

        if (lines.Count == 0)
            lines.Add(Line("No new mail"));

        lines.Add(TooltipLine.Themed("Click to open the mail", ThemeRoles.TextDisabled));

        return lines;
    }

    private IReadOnlyList<TooltipLine> ClockLines()
        => ShowsGameTime ? [Line("Game time"), TooltipLine.Themed("Click for the local time", ThemeRoles.TextDisabled)]
            : GameTimeSource() is null ? [Line("Local time")]
            : [Line("Local time"), TooltipLine.Themed("Click for the game time", ThemeRoles.TextDisabled)];

    private static TooltipLine Line(string text) => TooltipLine.Themed(text, ThemeRoles.Text);

    private HudBox AddBox(string name, Rectangle bounds, Func<IReadOnlyList<TooltipLine>> lines)
    {
        var box = new HudBox(name, bounds);
        box.Entered += hovered => Tooltip.ShowBelow(hovered, lines);
        box.Left += Tooltip.Hide;
        AddChild(box);

        return box;
    }

    private static RoleIcon AddIcon(HudBox box, string name, Rectangle bounds)
    {
        var icon = new RoleIcon { Name = name };
        icon.SetBounds(bounds);
        box.AddChild(icon);

        return icon;
    }
}
