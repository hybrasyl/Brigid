#region
using Brigid.Controls.Components;
using Brigid.Data.Models;
using Brigid.Extensions;
using Chaos.DarkAges.Definitions;
using Brigid.ViewModel;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Brigid.Controls.World.Hud.Panel;

/// <summary>
///     Extended stats panel (Shift+G). Shows AC, DMG, HIT, offense/defense elements, and magic resistance.
///     Normal HUD loads from "ExtraStatus" in _nstatus. Large HUD loads compact from _nstatur, expanding to _nstatus.
/// </summary>
public sealed class ExtendedStatsPanel : ExpandablePanel
{
    private const int IDX_ATTACK = 0;
    private const int IDX_DEFENSE = 1;
    private const int IDX_MAGIC = 2;
    private const int IDX_AC = 3;
    private const int IDX_DMG = 4;
    private const int IDX_HIT = 5;
    private const int LABEL_COUNT = 6;

    private static readonly string[] LABEL_NAMES =
    [
        "e_attack",
        "e_defense",
        "e_magic",
        "e_AC",
        "e_DMG",
        "e_HIT"
    ];

    private static readonly string[] ElementNames = BuildElementNames();

    private readonly UILabel?[] Labels = new UILabel?[LABEL_COUNT];
    private readonly long[] StatValues = new long[LABEL_COUNT];
    private Element OffenseElement = (Element)byte.MaxValue;
    private Element DefenseElement = (Element)byte.MaxValue;

    //expand repositioning — compact and expanded label layouts
    private LabelLayout[]? CompactLayouts;
    private bool[]? ExistsInCompact;
    private LabelLayout[]? ExpandedLayouts;

    public ExtendedStatsPanel(ControlPrefabSet statusPrefabSet)
    {
        Name = "ExtendedStats";
        Visible = false;

        //load background from "extrastatus" image in the prefab set.
        var extraLeft = 0;
        var extraTop = 0;

        if (statusPrefabSet.Contains("ExtraStatus"))
        {
            var prefab = statusPrefabSet["ExtraStatus"];
            var rect = prefab.Control.Rect;

            if (rect is not null)
            {
                var r = rect.Value;
                extraLeft = (int)r.Left;
                extraTop = (int)r.Top;
                Width = (int)r.Width;
                Height = (int)r.Height;
            }

            if (prefab.Images.Count > 0)
                Background = UiRenderer.Instance!.GetPrefabTexture(statusPrefabSet.Name, "ExtraStatus", 0);
        }

        //extended stat labels (e_ prefix) — positioned relative to extrastatus origin
        for (var i = 0; i < LABEL_COUNT; i++)
            Labels[i] = CreateOffsetLabel(
                statusPrefabSet,
                LABEL_NAMES[i],
                extraLeft,
                extraTop);

        Array.Fill(StatValues, long.MinValue);
    }

    private static string[] BuildElementNames()
    {
        var values = Enum.GetValues<Element>();
        var max = 0;

        foreach (var v in values)
        {
            var b = (int)v;

            if (b > max)
                max = b;
        }

        var names = new string[max + 1];

        foreach (var v in values)
            names[(int)v] = v.Name;

        return names;
    }

    /// <summary>
    ///     Configures expand support. The expanded prefab set provides the full-size ExtraStatus background and label
    ///     positions. Labels that only exist in the expanded prefab are created hidden and shown on expand.
    /// </summary>
    public void ConfigureExpand(ControlPrefabSet expandedPrefabSet)
    {
        Texture2D? expandedTexture = null;

        if (expandedPrefabSet.Contains("ExtraStatus") && (expandedPrefabSet["ExtraStatus"].Images.Count > 0))
            expandedTexture = UiRenderer.Instance!.GetPrefabTexture(expandedPrefabSet.Name, "ExtraStatus", 0);

        ConfigureExpand(expandedTexture);

        CompactLayouts = new LabelLayout[LABEL_COUNT];
        ExpandedLayouts = new LabelLayout[LABEL_COUNT];
        ExistsInCompact = new bool[LABEL_COUNT];

        //get expanded extrastatus origin for offset calculation
        var expandedExtraRect = expandedPrefabSet.Contains("ExtraStatus") ? expandedPrefabSet["ExtraStatus"].Control.Rect : null;

        var exLeft = expandedExtraRect is not null ? (int)expandedExtraRect.Value.Left : 0;
        var exTop = expandedExtraRect is not null ? (int)expandedExtraRect.Value.Top : 0;

        for (var i = 0; i < LABEL_COUNT; i++)
        {
            ExistsInCompact[i] = Labels[i] is not null;

            //create missing labels that only exist in the expanded prefab
            if (Labels[i] is null)
            {
                var exRect = PrefabPanel.GetRect(expandedPrefabSet, LABEL_NAMES[i]);

                if (exRect != Rectangle.Empty)
                {
                    Labels[i] = new UILabel
                    {
                        Name = LABEL_NAMES[i],
                        X = exRect.X - exLeft,
                        Y = exRect.Y - exTop,
                        Width = exRect.Width,
                        Height = exRect.Height,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Visible = false
                    };

                    AddChild(Labels[i]!);
                }
            }

            if (Labels[i] is not null)
                CompactLayouts[i] = new LabelLayout(
                    Labels[i]!.X,
                    Labels[i]!.Y,
                    Labels[i]!.Width,
                    Labels[i]!.Height);

            var expandedRect = PrefabPanel.GetRect(expandedPrefabSet, LABEL_NAMES[i]);

            ExpandedLayouts[i] = new LabelLayout(
                expandedRect.X - exLeft,
                expandedRect.Y - exTop,
                expandedRect.Width,
                expandedRect.Height);
        }
    }

    private UILabel? CreateOffsetLabel(
        ControlPrefabSet prefabSet,
        string name,
        int xOffset,
        int yOffset)
    {
        if (!prefabSet.Contains(name))
            return null;

        var rect = prefabSet[name].Control.Rect;

        if (rect is null)
            return null;

        var r = rect.Value;

        var label = new UILabel
        {
            Name = name,
            X = (int)r.Left - xOffset,
            Y = (int)r.Top - yOffset,
            Width = (int)r.Width,
            Height = (int)r.Height,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        AddChild(label);

        return label;
    }

    public override void SetExpanded(bool expanded)
    {
        base.SetExpanded(expanded);

        if (ExistsInCompact is null)
            return;

        var layouts = expanded ? ExpandedLayouts : CompactLayouts;

        if (layouts is null)
            return;

        for (var i = 0; i < LABEL_COUNT; i++)
        {
            if (Labels[i] is null)
                continue;

            if (!ExistsInCompact[i])
            {
                Labels[i]!.Visible = expanded;

                if (!expanded)
                    continue;
            }

            Labels[i]!.X = layouts[i].X;
            Labels[i]!.Y = layouts[i].Y;
            Labels[i]!.Width = layouts[i].Width;
            Labels[i]!.Height = layouts[i].Height;
        }
    }

    private void TrySetLabel(int index, long value)
    {
        if (StatValues[index] == value)
            return;

        StatValues[index] = value;

        if (Labels[index] is { } label)
            label.Text = value.ToString();
    }

    public void UpdateAttributes(PlayerAttributes attrs)
    {
        TrySetLabel(IDX_AC, attrs.Ac);
        TrySetLabel(IDX_DMG, attrs.Dmg);
        TrySetLabel(IDX_HIT, attrs.Hit);
        TrySetLabel(IDX_MAGIC, attrs.MagicResistance * 10);

        if (OffenseElement != attrs.OffenseElement)
        {
            OffenseElement = attrs.OffenseElement;

            if (Labels[IDX_ATTACK] is { } label)
                label.Text = ElementNames[(int)attrs.OffenseElement];
        }

        if (DefenseElement != attrs.DefenseElement)
        {
            DefenseElement = attrs.DefenseElement;

            if (Labels[IDX_DEFENSE] is { } label)
                label.Text = ElementNames[(int)attrs.DefenseElement];
        }
    }

    private record struct LabelLayout(
        int X,
        int Y,
        int Width,
        int Height);
}