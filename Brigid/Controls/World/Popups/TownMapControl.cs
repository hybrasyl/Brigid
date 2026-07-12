#region
using Brigid.Collections;
using Brigid.Controls.Components;
using Brigid.Data;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
#endregion

namespace Brigid.Controls.World.Popups;

/// <summary>
///     Display-only town map overlay. When a <c>town_maps</c> .datf pack provides a full-panel image for the current
///     map it is drawn as-is (no marker); otherwise it falls back to the legacy composite built from
///     <c>national.dat</c> assets — the national map background with an animated player marker. Triggered by T key or
///     HUD button, dismissed by Escape, T, or any click.
/// </summary>
public sealed class TownMapControl : UIPanel
{
    private const int FRAME_WIDTH = 568;
    private const int FRAME_HEIGHT = 406;
    private const float MARKER_FRAME_INTERVAL = 0.083f;

    private record struct TownMapEntry(int MapId, int X, int Y, int TileWidth, int TileHeight);

    private List<TownMapEntry>? CoordEntries;

    //uiimage children — created on show, removed on hide
    private UIImage? BackgroundImage;
    private UIImage? IconBarImage;
    private UIImage? TownImageLayer;
    private UIImage? NameLabelImage;
    private UIImage? MarkerImage;

    //marker animation frames (swapped onto markerimage.texture each tick)
    private Texture2D[]? MarkerFrames;

    //marker animation + projection state
    private int MarkerFrame;
    private float MarkerTimer;
    private TownMapEntry ActiveEntry;

    //click-to-dismiss tracking (requires down then up)
    private bool MouseDownReceived;

    public TownMapControl()
    {
        Width = FRAME_WIDTH;
        Height = FRAME_HEIGHT;
        X = (640 - FRAME_WIDTH) / 2;
        Y = (480 - FRAME_HEIGHT) / 2;
        Visible = false;
        UsesControlStack = true;
        ZIndex = 2;
    }

    /// <summary>
    ///     Shows the town map for the given map ID if a matching town image exists. Loads all assets from national.dat,
    ///     computes the player marker position, and sets Visible = true.
    /// </summary>
    public void Show(short mapId, int playerTileX, int playerTileY)
    {
        //modern path: a town_maps .datf pack supplies a complete, pre-composited full-panel image (frame + art +
        //name baked in). Draw it in place of the legacy national.dat five-layer composite. No player marker — a full
        //interactive town map is a later redesign. Authored at 568x406 so a native 1:1 draw fills the panel.
        var modernTexture = UiRenderer.Instance?.GetTownMapImage(mapId);

        if (modernTexture is not null)
        {
            ShowModern(modernTexture);

            return;
        }

        EnsureCoordsParsed();

        //find matching entry
        var entryIndex = -1;

        for (var i = 0; i < CoordEntries!.Count; i++)
            if (CoordEntries[i].MapId == mapId)
            {
                entryIndex = i;

                break;
            }

        if (entryIndex < 0)
            return;

        //check that the town spf exists before committing to show
        if (!DatArchives.National.TryGetValue($"_t{mapId}.spf", out _))
            return;

        var entry = CoordEntries[entryIndex];

        //clear previous children
        ClearLayers();

        //layer 1: background at (0, 0) — fills the panel
        BackgroundImage = LoadSpfAsImage("_t_back.spf");

        if (BackgroundImage is not null)
            AddChild(BackgroundImage);

        //layer 2: icon bar
        IconBarImage = LoadSpfAsImage("_t_icon.spf");

        if (IconBarImage is not null)
        {
            //center horizontally within the frame, position in lower area
            IconBarImage.X = (FRAME_WIDTH - IconBarImage.Width) / 2;
            IconBarImage.Y = 301;
            AddChild(IconBarImage);
        }

        //layer 3: town image — _tcoord.txt x is negated centering offset, y is vertical offset
        TownImageLayer = LoadSpfAsImage($"_t{mapId}.spf");

        if (TownImageLayer is not null)
        {
            TownImageLayer.X = -entry.X;
            TownImageLayer.Y = entry.Y;
            AddChild(TownImageLayer);
        }

        //layer 4: name label — centered horizontally, near top
        NameLabelImage = LoadSpfAsImage($"_t{mapId}n.spf");

        if (NameLabelImage is not null)
        {
            NameLabelImage.X = (FRAME_WIDTH - NameLabelImage.Width) / 2;
            NameLabelImage.Y = 24;
            AddChild(NameLabelImage);
        }

        //layer 5: player position marker
        ActiveEntry = entry;
        MarkerFrames = LoadEpfMarkerFrames("tmuser.epf");

        if (MarkerFrames is not null && TownImageLayer is not null)
        {
            MarkerImage = new UIImage
            {
                Texture = MarkerFrames[0],
                Width = MarkerFrames[0].Width,
                Height = MarkerFrames[0].Height
            };
            UpdateMarkerPosition(playerTileX, playerTileY);
            AddChild(MarkerImage);
        }

        MarkerFrame = 0;
        MarkerTimer = 0;
        MouseDownReceived = false;
        InputDispatcher.Instance?.PushControl(this);
        Visible = true;
    }

    /// <summary>
    ///     Shows a pack-provided full-panel town map: a single image child at the panel origin, no marker/name/icon
    ///     layers. The texture is a cached <see cref="UiRenderer" /> asset, so its disposal is a no-op — <see
    ///     cref="ClearLayers" /> just detaches it like the legacy layers.
    /// </summary>
    private void ShowModern(Texture2D texture)
    {
        ClearLayers();

        //ActiveEntry stays default and MarkerImage/MarkerFrames stay null — the modern path has no player marker, so
        //Update's marker animation and UpdateMarkerPosition both null-guard out. A full interactive town map (marker
        //included) is a later redesign; do not wire the legacy projection math against a full-panel image.
        TownImageLayer = new UIImage
        {
            Texture = texture,
            Width = texture.Width,
            Height = texture.Height,
            //centered so a pack that ships a non-568x406 image degrades gracefully; a correctly-authored full panel
            //(568x406) lands at (0, 0) and fills the frame exactly.
            X = (FRAME_WIDTH - texture.Width) / 2,
            Y = (FRAME_HEIGHT - texture.Height) / 2
        };
        AddChild(TownImageLayer);

        MarkerFrame = 0;
        MarkerTimer = 0;
        MouseDownReceived = false;
        InputDispatcher.Instance?.PushControl(this);
        Visible = true;
    }

    /// <summary>
    ///     Hides the town map and disposes all loaded assets.
    /// </summary>
    public void Hide()
    {
        InputDispatcher.Instance?.RemoveControl(this);
        Visible = false;
        ClearLayers();
    }

    /// <inheritdoc />
    public override void Dispose()
    {
        Hide();
        base.Dispose();
    }

    public override void OnKeyDown(KeyDownEvent e)
    {
        if (e.Key is Keys.Escape or Keys.T)
        {
            Hide();
            e.Handled = true;
        }
    }

    public override void OnMouseDown(MouseDownEvent e)
    {
        MouseDownReceived = true;
        e.Handled = true;
    }

    public override void OnMouseUp(MouseUpEvent e)
    {
        base.OnMouseUp(e);

        if (MouseDownReceived)
            Hide();
    }

    public override void Update(GameTime gameTime)
    {
        if (!Visible)
            return;

        //update marker position to follow player
        var player = WorldState.GetPlayerEntity();

        if (player is not null)
            UpdateMarkerPosition(player.TileX, player.TileY);

        //animate player marker
        MarkerTimer += (float)gameTime.ElapsedGameTime.TotalSeconds;

        if (MarkerTimer >= MARKER_FRAME_INTERVAL)
        {
            MarkerTimer -= MARKER_FRAME_INTERVAL;
            MarkerFrame = (MarkerFrame + 1) % 7;

            if (MarkerImage is not null && MarkerFrames is not null)
                MarkerImage.Texture = MarkerFrames[MarkerFrame];
        }

        base.Update(gameTime);
    }

    #region Asset Loading
    private static UIImage LoadSpfAsImage(string fileName)
    {
        var texture = UiRenderer.Instance!.GetNationalSpfTexture(fileName);

        return new UIImage
        {
            Texture = texture,
            Width = texture.Width,
            Height = texture.Height
        };
    }

    private static Texture2D[]? LoadEpfMarkerFrames(string fileName)
    {
        var frameCount = DataContext.UserControls.GetNationalEpfFrameCount(fileName);

        if (frameCount == 0)
            return null;

        var frames = new Texture2D[frameCount];

        for (var i = 0; i < frameCount; i++)
            frames[i] = UiRenderer.Instance!.GetNationalEpfTexture(fileName, i);

        return frames;
    }
    #endregion

    #region Coordinate Parsing
    private void EnsureCoordsParsed()
    {
        if (CoordEntries is not null)
            return;

        CoordEntries = [];

        if (!DatArchives.National.TryGetValue("_tcoord.txt", out var entry))
            return;

        var text = System.Text.Encoding.GetEncoding(949).GetString(entry.ToSpan());
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var line in lines)
        {
            var numbers = ExtractNumbers(line);

            if (numbers.Count >= 5)
                CoordEntries.Add(
                    new TownMapEntry(
                        numbers[0],
                        numbers[1],
                        numbers[2],
                        numbers[3],
                        numbers[4]));
        }
    }

    private static List<int> ExtractNumbers(string line)
    {
        var numbers = new List<int>();
        var i = 0;

        while (i < line.Length)
        {
            if (!char.IsDigit(line[i]) && (line[i] != '-'))
            {
                i++;

                continue;
            }

            var start = i;

            if (line[i] == '-')
            {
                if (((i + 1) < line.Length) && char.IsDigit(line[i + 1]))
                    i++;
                else
                {
                    i++;

                    continue;
                }
            }

            while ((i < line.Length) && char.IsDigit(line[i]))
                i++;

            if (int.TryParse(line.AsSpan(start, i - start), out var value))
                numbers.Add(value);
        }

        return numbers;
    }
    #endregion

    #region Marker Projection
    private void UpdateMarkerPosition(int playerTileX, int playerTileY)
    {
        if (MarkerImage is null || TownImageLayer is null)
            return;

        var imgX = TownImageLayer.X;
        var imgY = TownImageLayer.Y;
        var imgW = TownImageLayer.Width;
        var imgH = TownImageLayer.Height;
        var tileW = ActiveEntry.TileWidth;
        var tileH = ActiveEntry.TileHeight;
        var totalTiles = tileW + tileH;

        if (totalTiles == 0)
            return;

        var pivotX = (imgW * tileH) / totalTiles + imgX;

        var pixelX = ((imgX - 1 + imgW - pivotX) * playerTileX) / tileW
                     + pivotX
                     + ((imgX - pivotX) * playerTileY) / tileH;

        var pixelY = (((imgH * tileW) / totalTiles) * playerTileX) / tileW
                     + imgY
                     + (((imgH * tileH) / totalTiles) * playerTileY) / tileH;

        MarkerImage.X = pixelX - 6;
        MarkerImage.Y = pixelY - 19;
    }
    #endregion

    #region Cleanup
    private void ClearLayers()
    {
        //all textures are cachedtexture2d from uirenderer — dispose is a no-op, just detach
        BackgroundImage = null;
        IconBarImage = null;
        TownImageLayer = null;
        NameLabelImage = null;
        MarkerImage = null;
        MarkerFrames = null;

        //null out textures before clearing so uiimage.dispose doesn't try to release cached textures
        foreach (var child in Children)
            if (child is UIImage image)
                image.Texture = null;

        Children.Clear();
    }
    #endregion

}