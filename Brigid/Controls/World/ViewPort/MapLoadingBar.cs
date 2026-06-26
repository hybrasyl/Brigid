#region
using Brigid.Controls.Components;
#endregion

namespace Brigid.Controls.World.ViewPort;

/// <summary>
///     Map loading screen using _nloadm prefab. Shown during map transitions within the game world.
///     Uses _nloadb0.spf (start cap), _nloadb1.spf (fill), _nloadb2.spf (end cap) for the progress bar.
/// </summary>
public class MapLoadingBar : PrefabPanel
{
    private readonly UIImage? EndCap;
    private readonly UIProgressBar? FillBar;

    public MapLoadingBar()
        : base("_nloadm")
    {
        Name = "MapLoading";
        Visible = false;

        var cache = UiRenderer.Instance!;
        var startTexture = cache.GetSpfTexture("_nloadb0.spf");
        var fillTexture = cache.GetSpfTexture("_nloadb1.spf");
        var endTexture = cache.GetSpfTexture("_nloadb2.spf");

        var barY = Height - startTexture.Height - 20;
        var barX = 20;

        AddChild(
            new UIImage
            {
                Texture = startTexture,
                X = barX,
                Y = barY
            });

        FillBar = new UIProgressBar
        {
            X = barX + startTexture.Width,
            Y = barY,
            Width = fillTexture.Width,
            Height = fillTexture.Height,
            FillTexture = fillTexture
        };

        AddChild(FillBar);

        EndCap = new UIImage
        {
            Texture = endTexture,
            X = barX + startTexture.Width + fillTexture.Width,
            Y = barY,
            Visible = false
        };

        AddChild(EndCap);
    }

    public void SetProgress(float progress)
    {
        progress = Math.Clamp(progress, 0f, 1f);

        FillBar?.Percent = progress;

        EndCap?.Visible = progress >= 1f;
    }

    public void Show(float initialProgress = 0f)
    {
        SetProgress(initialProgress);
        Visible = true;
    }
}