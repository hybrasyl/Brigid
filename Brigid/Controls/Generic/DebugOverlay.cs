#region
using System.Diagnostics;
using System.Runtime;
using Brigid.Controls.Components;
using Brigid.Rendering;
using Brigid.Systems;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
#endregion

namespace Brigid.Controls.Generic;

/// <summary>
///     Debug overlay that draws colored outlines and centered names for all visible UI elements. Toggle with F11.
///     Also displays performance stats: frame time, GC collection counts, heap size, and Gen2 hitch detection.
///     Uses deferred batching and text caching to minimize draw calls and texture allocations.
/// </summary>
public static class DebugOverlay
{
    private const int FRAME_TIME_HISTORY = 180;
    private const float GRAPH_WIDTH = 180;
    private const float GRAPH_HEIGHT = 36;
    private const float GEN2_FLASH_DURATION_MS = 1000;
    private const int STATS_LINE_COUNT = 8;
    private const int STATS_LINE_PITCH = TextRenderer.CHAR_HEIGHT + 1;
    private const int STATS_BG_HEIGHT = STATS_LINE_COUNT * STATS_LINE_PITCH + 4;

    //per-line change detection — only rebuild the interpolated string when its inputs actually changed
    private static int StatsPrevFps = -1;
    private static float StatsPrevMs = -1f;
    private static long StatsPrevDrawCount = -1;
    private static long StatsPrevDebugDrawCount = -1;
    private static double StatsPrevHeapMb = -1;
    private static int StatsPrevG0 = -1;
    private static int StatsPrevG1 = -1;
    private static int StatsPrevG2 = -1;
    private static GCLatencyMode StatsPrevGcMode = (GCLatencyMode)(-1);
    //sentinel `long.MinValue` distinguishes "never set" from a legitimate null reading
    private static long? StatsPrevNetMs = long.MinValue;
    private static int StatsPrevResW = -1;
    private static int StatsPrevResH = -1;

    //live nudge tool — the grabbed element, its rect at grab time (to report cumulative deltas), and the readout cache.
    private static UIElement? NudgeTarget;
    private static int NudgeOrigX, NudgeOrigY, NudgeOrigW, NudgeOrigH;
    private static string NudgeLastPrint = "";
    private static TextElement? FocusReadout;
    private static TextElement? NudgeReadout;

    //every visible element under the grab point, topmost first; NumPad+ and NumPad- move the grab through it
    private static List<UIElement> NudgeStack = [];
    private static int NudgeStackIndex;

    //the shortcut list under the readouts; NumPad/ opens and closes it
    private static bool ShowHelp;
    private static TextElement[]? HelpLines;

    private static readonly string[] HelpText =
    [
        "F11 overlay   num1 boxes   num2 names   num3 world   num4 stats + text   num0 all on",
        "num5 grab what a click hits, or drop   ctrl+num5 grab any element (labels, titles)",
        "num+ / num-   next / previous element under the grab point",
        "arrows move   shift+arrows resize   the change prints to the console",
        "ctrl+g primitive gallery: every primitive and theme colour, with its role",
        "num/ hide these shortcuts"
    ];

    private static readonly float[] FrameTimeHistory = new float[FRAME_TIME_HISTORY];
    private static readonly Stopwatch FrameStopwatch = new();
    private static readonly Dictionary<UIElement, TextElement> TextElementCache = [];
    private static TextElement[]? StatsTextElement;
    private static int FrameTimeIndex;
    private static int LastGen0Count;
    private static int LastGen1Count;
    private static int LastGen2Count;
    private static int Gen2Delta;
    private static float Gen2FlashTimer;
    private static float LastFrameWorkMs;
    private static int FpsCounter;
    private static float FpsElapsed;
    private static long SnappedDrawCount;
    private static long DebugDrawCount;

    /// <summary>
    ///     Master toggle (F11). When false the whole debug layer is inert and the numpad sub-toggles do nothing. While true,
    ///     <see cref="UIPanel.DrawOverChild" /> is <see cref="DrawElement" />.
    /// </summary>
    public static bool IsActive
    {
        get;
        set
        {
            field = value;
            UIPanel.DrawOverChild = value ? DrawElement : null;
        }
    }

    /// <summary>Sub-toggle: colored element border rectangles. Gated under <see cref="IsActive" />.</summary>
    public static bool ShowUiBoxes { get; set; } = true;

    /// <summary>Sub-toggle: centered element name labels. Gated under <see cref="IsActive" />.</summary>
    public static bool ShowUiNames { get; set; } = true;

    /// <summary>Sub-toggle: world-space overlays (tile outlines, hitboxes, crosshair, hover tile). Gated under <see cref="IsActive" />.</summary>
    public static bool ShowWorld { get; set; } = true;

    /// <summary>
    ///     Sub-toggle: performance stats (frame graph, GC, heap, net) and the focus and shortcut readouts under them. Gated
    ///     under <see cref="IsActive" />.
    /// </summary>
    public static bool ShowPerf { get; set; } = true;

    /// <summary>
    ///     The four layer sub-toggles at once, so that a debug panel (the primitive gallery) can turn them off while it is
    ///     open and put them back when it closes.
    /// </summary>
    public static (bool UiBoxes, bool UiNames, bool World, bool Perf) Layers
    {
        get => (ShowUiBoxes, ShowUiNames, ShowWorld, ShowPerf);
        set => (ShowUiBoxes, ShowUiNames, ShowWorld, ShowPerf) = value;
    }

    /// <summary>True while an element is grabbed by the live nudge tool. Consumers (e.g. world movement) suppress arrow keys.</summary>
    public static bool HasNudgeSelection => NudgeTarget is not null;

    /// <summary>
    ///     Screen X position for the debug overlay.
    /// </summary>
    public static float X { get; set; } = 4;

    /// <summary>
    ///     Screen Y position for the debug overlay.
    /// </summary>
    public static float Y { get; set; } = 4;

    private static float GraphX => X;
    private static float GraphY => Y;
    private static float StatsX => X;
    private static float StatsY => Y + GRAPH_HEIGHT + 4;

    /// <summary>
    ///     Call at the start of Update. Starts timing a frame at the first Update after a Draw. With a fixed time step,
    ///     MonoGame runs catch-up Updates and skips Draws under load, so every Update before the next Draw is part of
    ///     the same frame.
    /// </summary>
    public static void BeginFrame()
    {
        if (!FrameStopwatch.IsRunning)
            FrameStopwatch.Restart();
    }

    private static void ClearCaches()
    {
        TextElementCache.Clear();
        StatsTextElement = null;
        ResetFrameCounters();

        //reset change-detection sentinels so the next activation rebuilds every line exactly once
        StatsPrevFps = -1;
        StatsPrevMs = -1f;
        StatsPrevDrawCount = -1;
        StatsPrevDebugDrawCount = -1;
        StatsPrevHeapMb = -1;
        StatsPrevG0 = -1;
        StatsPrevG1 = -1;
        StatsPrevG2 = -1;
        StatsPrevGcMode = (GCLatencyMode)(-1);
        StatsPrevNetMs = long.MinValue;
        StatsPrevResW = -1;
        StatsPrevResH = -1;

        //release any grabbed element so toggling the overlay off ends the nudge session
        NudgeTarget = null;
    }

    /// <summary>
    ///     Live UI nudge tool. While the overlay is up, NumPad5 grabs/releases the element under the cursor; arrow keys then
    ///     move it (X/Y) and Shift+arrows resize it (W/H), one virtual px per press. NumPad5 grabs what a click would hit.
    ///     Ctrl+NumPad5 grabs the topmost visible element of any kind, also a label or a title that takes no clicks.
    ///     NumPad+ and NumPad- then step through every element under the grab point. NumPad/ shows the shortcut list. The cumulative (dx, dy, dw, dh) prints
    ///     to the console as a paste-ready <c>Rectangle</c> so a baked prefab rect can be corrected without a rebuild loop.
    ///     Driven per-frame from <see cref="InputDispatcher.ProcessInput" />, which supplies the active screen's root + mouse.
    /// </summary>
    private static bool IsCtrlHeld() => InputBuffer.IsKeyHeld(Keys.LeftControl) || InputBuffer.IsKeyHeld(Keys.RightControl);

    //every visible element under the point, topmost first: an element's children come before the element, and a higher
    //ZIndex before a lower one. Unlike InputDispatcher.HitTest it keeps the elements that take no clicks.
    private static void CollectAt(UIPanel panel, int x, int y, List<UIElement> found)
    {
        panel.EnsureChildOrder();

        for (var i = panel.Children.Count - 1; i >= 0; i--)
        {
            var child = panel.Children[i];

            if (!child.Visible || !child.ContainsPoint(x, y))
                continue;

            if (child is UIPanel childPanel)
                CollectAt(childPanel, x, y, found);

            found.Add(child);
        }
    }

    private static void Grab(UIElement element)
    {
        NudgeTarget = element;
        NudgeOrigX = element.X;
        NudgeOrigY = element.Y;
        NudgeOrigW = element.Width;
        NudgeOrigH = element.Height;
        NudgeLastPrint = "";
    }

    //the dotted path of names from the screen root, as a ui_panels pack addresses the element; a child with no name
    //shows its type
    private static string PathOf(UIElement element)
    {
        var parts = new List<string>();

        for (var current = element; current?.Parent is not null; current = current.Parent)
            parts.Add(current.Name.Length > 0 ? current.Name : current.GetType().Name);

        parts.Reverse();

        return string.Join('.', parts);
    }

    public static void UpdateNudge(UIPanel root, int mouseX, int mouseY)
    {
        if (!IsActive)
            return;

        if (InputBuffer.WasKeyPressed(Keys.Divide))
            ShowHelp ^= true;

        //NumPad5 toggles the grab: release if holding one, else grab what a click would hit (Ctrl: the topmost element of
        //any kind). The stack under the cursor is kept, so NumPad+ and NumPad- can step to the elements above and below.
        if (InputBuffer.WasKeyPressed(Keys.NumPad5))
        {
            if (NudgeTarget is not null)
                NudgeTarget = null;
            else
            {
                var stack = new List<UIElement>();
                CollectAt(root, mouseX, mouseY, stack);

                var hit = IsCtrlHeld() ? stack.FirstOrDefault() : InputDispatcher.HitTest(root, mouseX, mouseY);

                if (hit is not null)
                {
                    if (!stack.Contains(hit))
                        stack.Insert(0, hit);

                    NudgeStack = stack;
                    NudgeStackIndex = stack.IndexOf(hit);
                    Grab(hit);
                }
            }
        }

        if (NudgeTarget is null)
            return;

        //step through the stack; each element reports its own change from the moment it is grabbed
        var step = InputBuffer.WasKeyPressed(Keys.Add) ? 1 : InputBuffer.WasKeyPressed(Keys.Subtract) ? -1 : 0;

        if ((step != 0) && (NudgeStack.Count > 1))
        {
            NudgeStackIndex = (NudgeStackIndex + step + NudgeStack.Count) % NudgeStack.Count;
            Grab(NudgeStack[NudgeStackIndex]);
        }

        //drop the grab if the element went away (panel hidden, relayout, screen switch)
        if (!NudgeTarget.Visible)
        {
            NudgeTarget = null;

            return;
        }

        var resize = InputBuffer.IsKeyHeld(Keys.LeftShift) || InputBuffer.IsKeyHeld(Keys.RightShift);

        //arrows move (X/Y); shift+arrows resize (W/H). Right/Down are positive on both axes.
        if (InputBuffer.WasKeyPressed(Keys.Right))
            if (resize) NudgeTarget.Width++; else NudgeTarget.X++;

        if (InputBuffer.WasKeyPressed(Keys.Left))
            if (resize) NudgeTarget.Width--; else NudgeTarget.X--;

        if (InputBuffer.WasKeyPressed(Keys.Down))
            if (resize) NudgeTarget.Height++; else NudgeTarget.Y++;

        if (InputBuffer.WasKeyPressed(Keys.Up))
            if (resize) NudgeTarget.Height--; else NudgeTarget.Y--;

        var name = PathOf(NudgeTarget);
        var print = $"[\"{name}\"] = new Rectangle({NudgeTarget.X - NudgeOrigX}, {NudgeTarget.Y - NudgeOrigY}, "
                  + $"{NudgeTarget.Width - NudgeOrigW}, {NudgeTarget.Height - NudgeOrigH})";

        if (print != NudgeLastPrint)
        {
            NudgeLastPrint = print;
            Console.WriteLine($"[nudge] {print}");
        }
    }

    /// <summary>
    ///     Draws the debug border and label for a single element. Called after each child draws, through
    ///     <see cref="UIPanel.DrawOverChild" /> from UIPanel.Draw, so debug overlays respect the natural z-order of controls.
    /// </summary>
    public static void DrawElement(SpriteBatch spriteBatch, UIElement element)
    {
        if (!IsActive || !element.Visible)
            return;

        var isNudgeTarget = ReferenceEquals(element, NudgeTarget);

        //the grabbed element always draws (highlight + name) even when the box/name sub-toggles are off
        if (!ShowUiBoxes && !ShowUiNames && !isNudgeTarget)
            return;

        var sx = element.ScreenX;
        var sy = element.ScreenY;
        var w = element.Width;
        var h = element.Height;

        //use texture dimensions as fallback when width/height are 0
        if ((w == 0) && (h == 0) && element is UIPanel { Background: not null } bgPanel)
        {
            w = bgPanel.Background.Width;
            h = bgPanel.Background.Height;
        }

        if ((w <= 0) || (h <= 0))
            return;

        var color = element switch
        {
            UIButton  => Color.Lime,
            UITextBox => Color.Cyan,
            UILabel   => Color.Yellow,
            UIImage   => Color.Magenta,
            UIPanel   => Color.Red,
            _         => Color.White
        };

        if (ShowUiBoxes || isNudgeTarget)
            UIElement.DrawBorder(
                spriteBatch,
                new Rectangle(
                    sx,
                    sy,
                    w,
                    h),
                isNudgeTarget ? Color.White : color * 0.8f);

        //grabbed element: a bright outset border so it reads as selected regardless of the box color
        if (isNudgeTarget)
            UIElement.DrawBorder(spriteBatch, new Rectangle(sx - 1, sy - 1, w + 2, h + 2), Color.Yellow);

        if (!ShowUiNames && !isNudgeTarget)
            return;

        var name = element.Name.Length > 0
            ? element.Name
            : element.GetType()
                     .Name;

        if (!TextElementCache.TryGetValue(element, out var cachedTextElement))
        {
            cachedTextElement = new TextElement();
            TextElementCache[element] = cachedTextElement;
        }

        cachedTextElement.Update(name, color);

        if (cachedTextElement.HasContent)
        {
            var tw = cachedTextElement.Width;
            var th = cachedTextElement.Height;
            var tx = sx + (w - tw) / 2;
            var ty = sy + (h - th) / 2;

            UIElement.DrawRect(
                spriteBatch,
                new Rectangle(
                    tx - 1,
                    ty - 1,
                    tw + 2,
                    th + 2),
                Color.Black * 0.66f);

            cachedTextElement.Draw(spriteBatch, new Vector2(tx, ty));
        }
    }

    private static void DrawFrameTimeGraph(SpriteBatch spriteBatch)
    {
        var sampleCount = Math.Min(FrameTimeIndex, FRAME_TIME_HISTORY);

        if (sampleCount < 2)
            return;

        var barWidth = GRAPH_WIDTH / FRAME_TIME_HISTORY;

        //33ms target line (30fps — anything above this is a serious hitch)
        var targetY33 = GraphY + GRAPH_HEIGHT - GRAPH_HEIGHT * (33f / 50f);

        //16.67ms target line (60fps)
        var targetY16 = GraphY + GRAPH_HEIGHT - GRAPH_HEIGHT * (16.67f / 50f);

        //60fps line
        UIElement.DrawRect(
            spriteBatch,
            new Rectangle(
                (int)GraphX,
                (int)targetY16,
                (int)GRAPH_WIDTH,
                1),
            Color.Lime * 0.4f);

        //30fps line
        UIElement.DrawRect(
            spriteBatch,
            new Rectangle(
                (int)GraphX,
                (int)targetY33,
                (int)GRAPH_WIDTH,
                1),
            Color.Red * 0.4f);

        //draw bars
        for (var i = 0; i < FRAME_TIME_HISTORY; i++)
        {
            var idx = (FrameTimeIndex - FRAME_TIME_HISTORY + i + FRAME_TIME_HISTORY) % FRAME_TIME_HISTORY;
            var ms = FrameTimeHistory[idx];

            if (ms <= 0)
                continue;

            //clamp to graph range (0-50ms)
            var normalized = Math.Min(ms / 50f, 1f);
            var barHeight = (int)(GRAPH_HEIGHT * normalized);
            var barX = (int)(GraphX + i * barWidth);
            var barY = (int)(GraphY + GRAPH_HEIGHT - barHeight);

            var barColor = ms > 33
                ? Color.Red
                : ms > 17
                    ? Color.Yellow
                    : Color.Lime;

            UIElement.DrawRect(
                spriteBatch,
                new Rectangle(
                    barX,
                    barY,
                    Math.Max((int)barWidth, 1),
                    barHeight),
                barColor * 0.8f);
        }
    }

    private static void DrawPerformanceStatsGeometry(SpriteBatch spriteBatch)
    {
        var statsHeight = STATS_BG_HEIGHT;

        UIElement.DrawRect(
            spriteBatch,
            new Rectangle(
                (int)GraphX - 2,
                (int)GraphY - 2,
                (int)GRAPH_WIDTH + 4,
                (int)GRAPH_HEIGHT + statsHeight + 8),
            Color.Black * 0.33f);

        //frame time graph
        DrawFrameTimeGraph(spriteBatch);

        //flash the whole stats area red-tinted when a gen2 collection just happened
        if (Gen2FlashTimer > 0)
        {
            var flashAlpha = Gen2FlashTimer / GEN2_FLASH_DURATION_MS * 0.3f;

            UIElement.DrawRect(
                spriteBatch,
                new Rectangle(
                    (int)GraphX - 2,
                    (int)GraphY - 2,
                    (int)GRAPH_WIDTH + 4,
                    (int)GRAPH_HEIGHT + statsHeight + 8),
                Color.Red * flashAlpha);
        }
    }

    private static void DrawPerformanceStatsText(SpriteBatch spriteBatch)
    {
        if (StatsTextElement is null)
        {
            StatsTextElement = new TextElement[STATS_LINE_COUNT];

            for (var i = 0; i < STATS_LINE_COUNT; i++)
                StatsTextElement[i] = new TextElement();
        }

        var heapMb = GC.GetTotalMemory(false) / (1024.0 * 1024.0);
        var gcServerMode = GCSettings.IsServerGC ? "Server" : "Workstation";
        var gcLatencyMode = GCSettings.LatencyMode;
        var netMs = LatencyMonitor.LatencyMs;
        var drawCount = SnappedDrawCount;
        var debugDrawCount = DebugDrawCount;
        var fps = FramesPerSecond;
        var ms = LastFrameWorkMs;

        //line 0 — FPS + frame time (0.05ms tolerance matches {ms:F1} display precision)
        if ((StatsPrevFps != fps) || (MathF.Abs(StatsPrevMs - ms) >= 0.05f))
        {
            StatsPrevFps = fps;
            StatsPrevMs = ms;

            var color = ms > 20
                ? Color.Red
                : ms > 17
                    ? Color.Yellow
                    : Color.Lime;

            StatsTextElement[0]
                .Update($"{fps} FPS  {ms:F1}ms", color);
        }

        //line 1 — draw count
        if (StatsPrevDrawCount != drawCount)
        {
            StatsPrevDrawCount = drawCount;

            var color = drawCount > 3000 ? Color.Yellow : Color.White;

            StatsTextElement[1]
                .Update($"Draws: {drawCount} (excl. debug)", color);
        }

        //line 2 — debug draw count
        if (StatsPrevDebugDrawCount != debugDrawCount)
        {
            StatsPrevDebugDrawCount = debugDrawCount;

            StatsTextElement[2]
                .Update($"Debug draws: {debugDrawCount}", Color.Gray);
        }

        //line 3 — heap + gc mode (0.05 MB tolerance matches {heapMb:F1} display precision)
        if (Math.Abs(StatsPrevHeapMb - heapMb) >= 0.05)
        {
            StatsPrevHeapMb = heapMb;

            StatsTextElement[3]
                .Update($"Heap: {heapMb:F1} MB  ({gcServerMode})", Color.White);
        }

        //line 4 — gc collection counts
        if ((StatsPrevG0 != LastGen0Count) || (StatsPrevG1 != LastGen1Count) || (StatsPrevG2 != LastGen2Count))
        {
            StatsPrevG0 = LastGen0Count;
            StatsPrevG1 = LastGen1Count;
            StatsPrevG2 = LastGen2Count;

            var color = Gen2Delta > 0 ? Color.Red : Color.White;

            StatsTextElement[4]
                .Update($"GC: G0={LastGen0Count} G1={LastGen1Count} G2={LastGen2Count}", color);
        }

        //line 5 — gc latency mode
        if (StatsPrevGcMode != gcLatencyMode)
        {
            StatsPrevGcMode = gcLatencyMode;

            StatsTextElement[5]
                .Update($"GC Mode: {gcLatencyMode}", Color.Gray);
        }

        //line 6 — network latency (TCP smoothed RTT from kernel; null when unavailable)
        if (StatsPrevNetMs != netMs)
        {
            StatsPrevNetMs = netMs;

            var netText = netMs.HasValue ? $"Net: {netMs.Value} ms" : "Net: —";
            var netColor = netMs.HasValue ? Color.White : Color.Gray;

            StatsTextElement[6]
                .Update(netText, netColor);
        }

        //line 7 — client resolution: actual window/backbuffer size and its scale over the virtual 640x480 target
        var pp = ChaosGame.Device.PresentationParameters;
        var resW = pp.BackBufferWidth;
        var resH = pp.BackBufferHeight;

        if ((StatsPrevResW != resW) || (StatsPrevResH != resH))
        {
            StatsPrevResW = resW;
            StatsPrevResH = resH;

            var scaleX = (float)resW / ChaosGame.VIRTUAL_WIDTH;
            var scaleY = (float)resH / ChaosGame.VIRTUAL_HEIGHT;

            //single scale when the aspect is locked (per-axis equal); show both axes when maximized off-ratio
            var scaleText = MathF.Abs(scaleX - scaleY) < 0.01f ? $"{scaleX:F2}x" : $"{scaleX:F2}x{scaleY:F2}";

            StatsTextElement[7]
                .Update($"Res: {resW}x{resH}  ({scaleText} of {ChaosGame.VIRTUAL_WIDTH}x{ChaosGame.VIRTUAL_HEIGHT})", Color.White);
        }

        var y = StatsY;

        for (var i = 0; i < StatsTextElement.Length; i++)
            if (StatsTextElement[i].HasContent)
            {
                StatsTextElement[i]
                    .Draw(spriteBatch, new Vector2(StatsX, y));
                y += StatsTextElement[i].Height + 1;
            }
    }

    /// <summary>
    ///     Draws performance stats (frame time graph, GC, heap). Called as a separate top-level pass.
    /// </summary>
    /// <param name="transform">
    ///     Virtual→native scale for the pass this draws in. Supplied because the stats draw at native resolution with
    ///     the rest of the UI: positions stay in virtual coordinates and the transform scales them, while FontEngine
    ///     rasterizes glyphs at native size. Drawn into the virtual render target instead, the text was point-upscaled
    ///     along with the world and came out blurry against the crisp UI beside it.
    ///     <para>
    ///         Deliberately required rather than defaulting to null: null is exactly the untransformed, virtual-target
    ///         draw this exists to eliminate, so the compiler should not let a caller back into it.
    ///     </para>
    /// </param>
    public static void DrawStats(SpriteBatch spriteBatch, Matrix transform)
    {
        if (!IsActive)
            return;

        spriteBatch.Begin(samplerState: GlobalSettings.Sampler, transformMatrix: transform);

        if (ShowPerf)
        {
            DrawPerformanceStatsGeometry(spriteBatch);
            DrawPerformanceStatsText(spriteBatch);
        }

        //the readouts stack under the stats block (or at the top when perf is off), one line each
        var readoutY = (int)(ShowPerf ? StatsY + STATS_BG_HEIGHT + 4 : GraphY);

        //nudge readout draws whenever something is grabbed, independent of the perf sub-toggle
        readoutY = DrawNudgeReadout(spriteBatch, readoutY);

        if (ShowPerf)
        {
            //dispatcher focus state — names the culprit when keyboard focus wedges
            readoutY = DrawFocusReadout(spriteBatch, readoutY);

            DrawHelp(spriteBatch, readoutY);
        }

        spriteBatch.End();

        DebugDrawCount = ChaosGame.Device.Metrics.DrawCount - SnappedDrawCount;
    }

    //paste-ready (dx, dy, dw, dh) readout for the grabbed element: its path, its type and its place in the stack
    private static int DrawNudgeReadout(SpriteBatch spriteBatch, int y)
    {
        if (NudgeTarget is null)
            return y;

        var text = $"NUDGE {PathOf(NudgeTarget)} ({NudgeTarget.GetType().Name}) {NudgeStackIndex + 1}/{NudgeStack.Count}"
                 + $"  d({NudgeTarget.X - NudgeOrigX},{NudgeTarget.Y - NudgeOrigY},"
                 + $"{NudgeTarget.Width - NudgeOrigW},{NudgeTarget.Height - NudgeOrigH})";

        NudgeReadout ??= new TextElement();
        NudgeReadout.Update(text, Color.Yellow);

        return DrawReadoutLine(spriteBatch, NudgeReadout, y);
    }

    //one readout line on a dark band; returns the y of the next line
    private static int DrawReadoutLine(SpriteBatch spriteBatch, TextElement line, int y)
    {
        if (!line.HasContent)
            return y;

        UIElement.DrawRect(spriteBatch, new Rectangle((int)StatsX - 2, y - 1, line.Width + 4, line.Height + 2), Color.Black * 0.66f);
        line.Draw(spriteBatch, new Vector2(StatsX, y));

        return y + line.Height + 4;
    }

    //the shortcut list, or a one-line hint while it is closed
    private static void DrawHelp(SpriteBatch spriteBatch, int y)
    {
        if (HelpLines is null)
        {
            HelpLines = new TextElement[HelpText.Length + 1];

            for (var i = 0; i < HelpLines.Length; i++)
                HelpLines[i] = new TextElement();
        }

        if (!ShowHelp)
        {
            HelpLines[^1].Update("num/ shortcuts", Color.LightGray);
            DrawReadoutLine(spriteBatch, HelpLines[^1], y);

            return;
        }

        for (var i = 0; i < HelpText.Length; i++)
        {
            HelpLines[i].Update(HelpText[i], Color.White);
            y = DrawReadoutLine(spriteBatch, HelpLines[i], y);
        }
    }

    //live keyboard-routing state: the dispatcher's explicit-focus target, the chat box's own focus
    //flag, and the control-stack top. When focus wedges (caret blinking but keys going elsewhere),
    //this line names which element is holding the routing.
    private static int DrawFocusReadout(SpriteBatch spriteBatch, int y)
    {
        if (InputDispatcher.Instance is not { } dispatcher)
            return y;

        var explicitEl = dispatcher.ExplicitFocus;

        var explicitName = explicitEl is null
            ? "none"
            : explicitEl.Name.Length > 0
                ? explicitEl.Name
                : explicitEl.GetType().Name;

        var top = dispatcher.TopControl;

        var topName = top is null
            ? "none"
            : top.Name.Length > 0
                ? top.Name
                : top.GetType().Name;

        var chatFocused = dispatcher.ChatInputTextBox?.IsFocused == true;

        var text = $"FOCUS explicit={explicitName}  chatbox={(chatFocused ? "focused" : "-")}"
                 + $"  stacktop={topName}({dispatcher.ControlStackCount})";

        FocusReadout ??= new TextElement();
        FocusReadout.Update(text, Color.Cyan);

        return DrawReadoutLine(spriteBatch, FocusReadout, y);
    }

    /// <summary>
    ///     Call at the end of Draw. Stops timing the frame's Update+Draw work, samples it for the frame-time graph, and
    ///     counts the drawn frame for the FPS readout.
    /// </summary>
    public static void EndFrame()
    {
        FrameStopwatch.Stop();
        LastFrameWorkMs = (float)FrameStopwatch.Elapsed.TotalMilliseconds;

        if (!IsActive)
            return;

        FrameTimeHistory[FrameTimeIndex % FRAME_TIME_HISTORY] = LastFrameWorkMs;
        FrameTimeIndex++;
        FpsCounter++;
    }

    /// <summary>
    ///     The drawn frames in the last full second.
    /// </summary>
    internal static int FramesPerSecond { get; private set; }

    /// <summary>
    ///     Starts the FPS count again from zero, so frames counted before the overlay closed do not go into the next reading.
    /// </summary>
    internal static void ResetFrameCounters()
    {
        FpsCounter = 0;
        FpsElapsed = 0f;
        FramesPerSecond = 0;
    }

    /// <summary>
    ///     Captures the GPU draw count before the debug overlay renders its own draws. Call immediately before Draw() so the
    ///     reported count excludes debug overlay draws.
    /// </summary>
    public static void SnapshotDrawCount() => SnappedDrawCount = ChaosGame.Device.Metrics.DrawCount;

    public static void Toggle()
    {
        IsActive = !IsActive;

        if (!IsActive)
            ClearCaches();
    }

    /// <summary>
    ///     Call once per Update to publish the FPS each second and sample GC counters. EndFrame counts the frames.
    /// </summary>
    public static void Update(GameTime gameTime)
    {
        if (!IsActive)
            return;

        FpsElapsed += (float)gameTime.ElapsedGameTime.TotalMilliseconds;

        if (FpsElapsed >= 1000f)
        {
            FramesPerSecond = FpsCounter;
            FpsCounter = 0;
            FpsElapsed -= 1000f;
        }

        //gc collection tracking
        var g0 = GC.CollectionCount(0);
        var g1 = GC.CollectionCount(1);
        var g2 = GC.CollectionCount(2);

        Gen2Delta = g2 - LastGen2Count;

        if (Gen2Delta > 0)
            Gen2FlashTimer = GEN2_FLASH_DURATION_MS;

        LastGen0Count = g0;
        LastGen1Count = g1;
        LastGen2Count = g2;

        if (Gen2FlashTimer > 0)
            Gen2FlashTimer -= (float)gameTime.ElapsedGameTime.TotalMilliseconds;
    }
}