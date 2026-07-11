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
    //cosmetic fill pacing. the visible wait during a world transfer is the network reconnect, which has no
    //real per-unit progress, so the bar eases asymptotically toward a sub-full cap to convey "working". the
    //real tile preload (SetProgress) snaps it to 100% and hides it the moment the destination map arrives.
    private const float CosmeticCap = 0.9f;
    private const float CosmeticTauMs = 1500f;

    private readonly UIImage? EndCap;
    private readonly UIProgressBar? FillBar;
    private bool CosmeticActive;
    private float CosmeticElapsedMs;

    public MapLoadingBar()
        : base("_nloadm")
    {
        Name = "MapLoading";
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

    /// <summary>
    ///     Advances the cosmetic fill while the screen is visible. No-op during single-tick in-world map
    ///     changes (shown and hidden within one frame); only spans frames during a network transfer.
    /// </summary>
    public void Tick(float elapsedMs)
    {
        if (!Visible || !CosmeticActive)
            return;

        CosmeticElapsedMs += elapsedMs;
        SetProgress(CosmeticCap * (1f - MathF.Exp(-CosmeticElapsedMs / CosmeticTauMs)));
    }

    public void Show(float initialProgress = 0f)
    {
        CosmeticActive = true;
        CosmeticElapsedMs = 0f;
        SetProgress(initialProgress);
        Visible = true;
    }
}