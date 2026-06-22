#region
using System.Diagnostics;
using System.Runtime;
using Chaos.Client.Controls.Components;
using Chaos.Client.Rendering;
using Chaos.Client.Systems;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Chaos.Client.Controls.Generic;

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
    private const int STATS_LINE_COUNT = 7;
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
    private static int DisplayFps;
    private static float FpsElapsed;
    private static long SnappedDrawCount;
    private static long DebugDrawCount;

    public static bool IsActive { get; set; }

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
    ///     Call at the start of Update to capture the previous frame's work time and begin timing the new frame.
    /// </summary>
    public static void BeginFrame()
    {
        LastFrameWorkMs = (float)FrameStopwatch.Elapsed.TotalMilliseconds;
        FrameStopwatch.Restart();
    }

    private static void ClearCaches()
    {
        TextElementCache.Clear();
        StatsTextElement = null;

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
    }

    /// <summary>
    ///     Draws the debug border and label for a single element. Called inline from UIPanel.Draw after each child draws, so
    ///     debug overlays respect the natural z-order of controls.
    /// </summary>
    public static void DrawElement(SpriteBatch spriteBatch, UIElement element)
    {
        if (!IsActive || !element.Visible)
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

        UIElement.DrawBorder(
            spriteBatch,
            new Rectangle(
                sx,
                sy,
                w,
                h),
            color * 0.8f);

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
        var fps = DisplayFps;
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
    public static void DrawStats(SpriteBatch spriteBatch)
    {
        if (!IsActive)
            return;

        spriteBatch.Begin(samplerState: GlobalSettings.Sampler);

        DrawPerformanceStatsGeometry(spriteBatch);
        DrawPerformanceStatsText(spriteBatch);

        spriteBatch.End();

        DebugDrawCount = ChaosGame.Device.Metrics.DrawCount - SnappedDrawCount;
    }

    /// <summary>
    ///     Call at the end of Draw to stop timing the current frame's Update+Draw work.
    /// </summary>
    public static void EndFrame() => FrameStopwatch.Stop();

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
    ///     Call once per frame from Update() to sample frame time and GC counters.
    /// </summary>
    public static void Update(GameTime gameTime)
    {
        if (!IsActive)
            return;

        FrameTimeHistory[FrameTimeIndex % FRAME_TIME_HISTORY] = LastFrameWorkMs;
        FrameTimeIndex++;

        //fps counter — count actual frames per second
        FpsCounter++;
        FpsElapsed += (float)gameTime.ElapsedGameTime.TotalMilliseconds;

        if (FpsElapsed >= 1000f)
        {
            DisplayFps = FpsCounter;
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