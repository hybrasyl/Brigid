#region
using Brigid.Controls.Components;
#endregion

namespace Brigid.Controls.World.ViewPort;

/// <summary>
///     Loading/progress screen using _nload prefab. Displays a background with an animated progress bar
///     composed of _nloadb0.spf (start cap), _nloadb1.spf (fill), _nloadb2.spf (end cap).
/// </summary>
public class LoadingBar : PrefabPanel
{
    private readonly UIImage? EndCap;
    private readonly UIProgressBar? FillBar;

    public LoadingBar()
        : base("_nload")
    {
        Name = "Loading";
        Visible = false;

        var cache = UiRenderer.Instance!;
        var startTexture = cache.GetSpfTexture("_nloadb0.spf");
        var fillTexture = cache.GetSpfTexture("_nloadb1.spf");
        var endTexture = cache.GetSpfTexture("_nloadb2.spf");

        //the prefab defines the bar-piece slots as named controls (Head/Body/Tail) recessed into the art;
        //use them so the bar sits in the groove instead of a hardcoded guess.
        var headRect = GetRect("Head");
        var bodyRect = GetRect("Body");
        var tailRect = GetRect("Tail");

        //caps need explicit Width/Height — a 0x0 element is fully clipped and never draws (UIElement.Draw).
        AddChild(
            new UIImage
            {
                Texture = startTexture,
                X = headRect.X,
                Y = headRect.Y,
                Width = headRect.Width,
                Height = headRect.Height
            });

        FillBar = new UIProgressBar
        {
            X = bodyRect.X,
            Y = bodyRect.Y,
            Width = bodyRect.Width,
            Height = bodyRect.Height,
            FillTexture = fillTexture
        };

        AddChild(FillBar);

        EndCap = new UIImage
        {
            Texture = endTexture,
            X = tailRect.X,
            Y = tailRect.Y,
            Width = tailRect.Width,
            Height = tailRect.Height,
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