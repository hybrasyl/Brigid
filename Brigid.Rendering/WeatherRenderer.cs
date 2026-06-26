#region
using Brigid.Data;
using Brigid.Rendering.Models;
using Chaos.DarkAges.Definitions;
using DALib.Drawing;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Graphics = DALib.Drawing.Graphics;
#endregion

namespace Brigid.Rendering;

/// <summary>
///     Weather overlay for snow and rain. Driven by the low nibble of the <see cref="MapFlags"/> byte:
///     0 = none, 1 = Snow, 2 = Rain (client-only — retail treats case 2 as a no-op), 3 = Darkness
///     (handled by <see cref="DarknessRenderer"/>, so this renderer stays inactive on value 3). The flag
///     byte's high nibble is unrelated — NoTabMap and SnowTileset are separate concerns.
///     See docs/re_notes/map_flags.md for the full encoding.
/// </summary>
public sealed class WeatherRenderer : IDisposable
{
    // ============================================================
    // Weather tunables — adjust for visual feel. Change these,
    // rebuild, and the effect reflects the new values. No runtime
    // config surface.
    // ============================================================

    // Snow
    private const int SNOW_PARTICLE_COUNT = 50;    // retail uses ~304 across 4 types
    private const float SNOW_MIN_VELOCITY_Y = 30f; // px/sec
    private const float SNOW_MAX_VELOCITY_Y = 70f;
    private const float SNOW_DRIFT_X = 10f;        // horizontal drift range (+/-)
    private const float SNOW_FRAME_DURATION = 0.10f; // seconds per animation frame for multi-frame types

    // Rain
    private const float RAIN_FALL_SPEED = 400f;    // px/sec vertical fall speed
    private const int RAIN_COLUMN_COUNT = 5;       // each rain row is drawn as N vertical strips in a random order

    // ============================================================

    private readonly Random SnowRandom = new();

    //cached legend01.pal from legend.dat — used for both snow and rain per ChaosAssetManager routing
    private Palette? Legend01Palette;

    private MapFlags CurrentMapFlags;

    //low nibble of the map-flags byte — 1=Snow, 2=Rain, 3=Darkness (dark handled elsewhere)
    private byte WeatherNibble => (byte)((byte)CurrentMapFlags & 0x0F);

    // Snow state — jagged array: SnowTypeFrames[typeIndex][animFrame]. snowa00/01 have 1 frame each,
    // snowa02/03 have 4 frames each, so inner-array lengths vary.
    private Texture2D[][]? SnowTypeFrames;
    private SnowParticle[]? SnowParticles;
    private int LastViewportWidth;
    private int LastViewportHeight;

    // Rain state — queue of falling rain rows, each a full texture copy with its own shuffled columns
    private Texture2D? RainTexture;
    private readonly List<RainRow> RainRows = [];

    //each rain row is one instance of the rain texture falling down the screen with an independent
    //column ordering. new rows are added above as old ones slide off the top position, and rows are
    //removed once they fall fully below the viewport.
    private struct RainRow
    {
        public float Y;
        public int[] Permutation;
    }

    /// <summary>
    ///     Whether a weather effect is currently active (snow or rain mode with assets loaded).
    /// </summary>
    public bool IsActive => WeatherNibble is 0x01 or 0x02;

    /// <summary>
    ///     Call on map change. Compares nibbles; if the weather mode changed, releases old textures
    ///     and loads new ones.
    /// </summary>
    public void OnMapChanged(MapFlags flags)
    {
        var oldNibble = WeatherNibble;
        CurrentMapFlags = flags;
        var newNibble = WeatherNibble;

        if (oldNibble == newNibble)
            return;

        ReleaseTextures();

        switch (newNibble)
        {
            case 0x01:
                LoadSnowAssets();

                break;

            case 0x02:
                LoadRainAsset();

                break;
        }
    }

    /// <summary>
    ///     Call each frame before <see cref="Draw"/>. Advances particle positions or rain scroll offset.
    /// </summary>
    public void Update(GameTime gameTime, Rectangle viewport)
    {
        var nibble = WeatherNibble;

        if (nibble is not (0x01 or 0x02))
            return;

        var dt = (float)gameTime.ElapsedGameTime.TotalSeconds;

        switch (nibble)
        {
            case 0x01:
                UpdateSnow(dt, viewport);

                break;

            case 0x02:
                UpdateRain(dt, viewport);

                break;
        }
    }

    /// <summary>
    ///     Draws the current weather overlay inside <paramref name="viewport"/>. Returns early when the
    ///     mode is none (nibble 0 or 3).
    /// </summary>
    public void Draw(SpriteBatch spriteBatch, Rectangle viewport)
    {
        var nibble = WeatherNibble;

        if (nibble is not (0x01 or 0x02))
            return;

        switch (nibble)
        {
            case 0x01:
                DrawSnow(spriteBatch);

                break;

            case 0x02:
                DrawRain(spriteBatch, viewport);

                break;
        }
    }

    /// <inheritdoc />
    public void Dispose() => ReleaseTextures();

    private void ReleaseTextures()
    {
        if (SnowTypeFrames is not null)
        {
            for (var t = 0; t < SnowTypeFrames.Length; t++)
            {
                var frames = SnowTypeFrames[t];

                for (var f = 0; f < frames.Length; f++)
                    frames[f].Dispose();
            }

            SnowTypeFrames = null;
        }

        RainTexture?.Dispose();
        RainTexture = null;
        RainRows.Clear();
        SnowParticles = null;
        LastViewportWidth = 0;
        LastViewportHeight = 0;
    }

    // ============================================================
    // Snow
    // ============================================================

    private Palette? GetLegend01Palette()
    {
        if (Legend01Palette is not null)
            return Legend01Palette;

        if (!DatArchives.Legend.TryGetValue("legend01.pal", out var entry))
            return null;

        Legend01Palette = Palette.FromEntry(entry);

        return Legend01Palette;
    }

    private void LoadSnowAssets()
    {
        var palette = GetLegend01Palette();

        if (palette is null)
        {
            CurrentMapFlags &= ~(MapFlags)0x0F;

            return;
        }

        //load all frames from each snowa file (snowa00/01 have 1 frame, snowa02/03 have 4 frames)
        var typeFrames = new List<Texture2D[]>(4);

        for (var i = 0; i < 4; i++)
        {
            var name = $"snowa{i:D2}.epf";

            if (!DatArchives.Legend.TryGetValue(name, out var entry))
                continue;

            var epf = EpfFile.FromEntry(entry);

            if (epf.Count == 0)
                continue;

            var framesForType = new Texture2D[epf.Count];

            for (var f = 0; f < epf.Count; f++)
            {
                using var image = Graphics.RenderImage(epf[f], palette);
                framesForType[f] = TextureConverter.ToTexture2D(image);
            }

            typeFrames.Add(framesForType);
        }

        if (typeFrames.Count == 0)
        {
            CurrentMapFlags &= ~(MapFlags)0x0F;

            return;
        }

        SnowTypeFrames = typeFrames.ToArray();
        SnowParticles = new SnowParticle[SNOW_PARTICLE_COUNT];
    }

    private void UpdateSnow(float dt, Rectangle viewport)
    {
        if (SnowTypeFrames is null || SnowParticles is null || (viewport.Width <= 0) || (viewport.Height <= 0))
            return;

        //respawn all particles when viewport changes (hud swap, map change)
        if ((viewport.Width != LastViewportWidth) || (viewport.Height != LastViewportHeight))
        {
            for (var i = 0; i < SnowParticles.Length; i++)
                SnowParticles[i] = RandomSnowParticle(viewport, false);

            LastViewportWidth = viewport.Width;
            LastViewportHeight = viewport.Height;
        }

        //each particle keeps its TypeIndex (shape) for life but advances its own animation timer
        //for multi-frame types (snowa02/03 have 4 frames each; snowa00/01 have just 1)
        for (var i = 0; i < SnowParticles.Length; i++)
        {
            ref var p = ref SnowParticles[i];

            p.Position.Y += p.VelocityY * dt;
            p.Position.X += p.VelocityX * dt;

            var frameCount = SnowTypeFrames[p.TypeIndex].Length;

            if (frameCount > 1)
            {
                p.FrameTimer += dt;

                while (p.FrameTimer >= SNOW_FRAME_DURATION)
                {
                    p.FrameTimer -= SNOW_FRAME_DURATION;
                    p.Frame = (p.Frame + 1) % frameCount;
                }
            }

            if (p.Position.Y > viewport.Bottom)
                p = RandomSnowParticle(viewport, true);
        }
    }

    private SnowParticle RandomSnowParticle(Rectangle viewport, bool spawnAbove)
    {
        var x = (float)SnowRandom.Next(viewport.Left, viewport.Right);

        var y = spawnAbove
            ? viewport.Top - SnowRandom.Next(0, 30)
            : SnowRandom.Next(viewport.Top, viewport.Bottom);

        var vy = SNOW_MIN_VELOCITY_Y + ((float)SnowRandom.NextDouble() * (SNOW_MAX_VELOCITY_Y - SNOW_MIN_VELOCITY_Y));
        var vx = (((float)SnowRandom.NextDouble() * 2f) - 1f) * SNOW_DRIFT_X;

        var typeIndex = SnowRandom.Next(0, SnowTypeFrames!.Length);
        var frameCount = SnowTypeFrames[typeIndex].Length;

        return new SnowParticle
        {
            Position = new Vector2(x, y),
            VelocityY = vy,
            VelocityX = vx,
            TypeIndex = typeIndex,
            Frame = SnowRandom.Next(0, frameCount),
            FrameTimer = (float)SnowRandom.NextDouble() * SNOW_FRAME_DURATION
        };
    }

    private void DrawSnow(SpriteBatch spriteBatch)
    {
        if (SnowTypeFrames is null || SnowParticles is null)
            return;

        for (var i = 0; i < SnowParticles.Length; i++)
        {
            var p = SnowParticles[i];
            var frames = SnowTypeFrames[p.TypeIndex];
            var frame = frames[p.Frame % frames.Length];

            spriteBatch.Draw(frame, p.Position, Color.White);
        }
    }

    // ============================================================
    // Rain
    // ============================================================

    private void LoadRainAsset()
    {
        var palette = GetLegend01Palette();

        if (palette is null)
        {
            CurrentMapFlags &= ~(MapFlags)0x0F;

            return;
        }

        if (!DatArchives.Legend.TryGetValue("rain01.epf", out var entry))
        {
            CurrentMapFlags &= ~(MapFlags)0x0F;

            return;
        }

        var epf = EpfFile.FromEntry(entry);

        if (epf.Count == 0)
        {
            CurrentMapFlags &= ~(MapFlags)0x0F;

            return;
        }

        using var image = Graphics.RenderImage(epf[0], palette);
        RainTexture = TextureConverter.ToTexture2D(image);
        RainRows.Clear();
    }

    private int[] GenerateColumnPermutation()
    {
        var arr = new int[RAIN_COLUMN_COUNT];

        for (var i = 0; i < RAIN_COLUMN_COUNT; i++)
            arr[i] = i;

        //fisher-yates shuffle — a single permutation applied consistently across every tile and wrap
        //so there's no mid-screen seam. a fresh shuffle is generated each time rain re-activates.
        for (var i = RAIN_COLUMN_COUNT - 1; i > 0; i--)
        {
            var j = SnowRandom.Next(i + 1);
            (arr[i], arr[j]) = (arr[j], arr[i]);
        }

        return arr;
    }

    private void UpdateRain(float dt, Rectangle viewport)
    {
        if (RainTexture is null)
            return;

        var texH = RainTexture.Height;

        //first call after rain activated — seed the queue with two rows at the top of the viewport
        if (RainRows.Count == 0)
        {
            RainRows.Add(new RainRow { Y = viewport.Y, Permutation = GenerateColumnPermutation() });
            RainRows.Add(new RainRow { Y = viewport.Y + texH, Permutation = GenerateColumnPermutation() });
        }

        //advance every row
        var dy = RAIN_FALL_SPEED * dt;

        for (var i = 0; i < RainRows.Count; i++)
        {
            var row = RainRows[i];
            row.Y += dy;
            RainRows[i] = row;
        }

        //as the topmost row's top edge falls below the top of the viewport, insert a fresh randomized
        //row above it so its bottom sits flush with the old topmost's top. while-loop handles large dt.
        while ((RainRows.Count > 0) && (RainRows[0].Y > viewport.Y))
            RainRows.Insert(
                0,
                new RainRow
                {
                    Y = RainRows[0].Y - texH,
                    Permutation = GenerateColumnPermutation()
                });

        //drop rows whose top has passed the bottom of the viewport
        var viewportBottom = viewport.Y + viewport.Height;

        for (var i = RainRows.Count - 1; i >= 0; i--)
            if (RainRows[i].Y > viewportBottom)
                RainRows.RemoveAt(i);
    }

    private void DrawRain(SpriteBatch spriteBatch, Rectangle viewport)
    {
        if (RainTexture is null || (viewport.Width <= 0) || (viewport.Height <= 0) || (RainRows.Count == 0))
            return;

        var texW = RainTexture.Width;
        var texH = RainTexture.Height;
        var colW = texW / RAIN_COLUMN_COUNT;
        var tilesX = ((viewport.Width + texW) - 1) / texW;

        //each row is a full texture copy falling at its own Y with its own column permutation
        for (var r = 0; r < RainRows.Count; r++)
        {
            var row = RainRows[r];
            var y = (int)row.Y;
            var perm = row.Permutation;

            for (var tx = 0; tx < tilesX; tx++)
            {
                var baseX = viewport.X + (tx * texW);

                for (var c = 0; c < RAIN_COLUMN_COUNT; c++)
                {
                    var srcX = perm[c] * colW;
                    var destX = baseX + (c * colW);
                    var srcRect = new Rectangle(srcX, 0, colW, texH);
                    var destRect = new Rectangle(destX, y, colW, texH);

                    spriteBatch.Draw(RainTexture, destRect, srcRect, Color.White);
                }
            }
        }
    }
}
