#region
using Brigid.Controls.Components;
using Brigid.Data.Repositories;
using Chaos.DarkAges.Definitions;
using Microsoft.Xna.Framework.Input;
#endregion

namespace Brigid.Controls.World.Popups;

/// <summary>
///     Social status picker popup built from the lemot prefab. The prefab defines Emot0 and Emot1 as templates;
///     Emot2-Emot7 are generated following the same layout pattern (3 frames per status from emot000.epf). Hovering shows
///     frame[1] and updates the Description label. Clicking selects that status.
/// </summary>
public sealed class SocialStatusControl : PrefabPanel
{
    private const int STATUS_COUNT = 8;
    private const int FRAMES_PER_STATUS = 3;
    private readonly UILabel? DescriptionLabel;
    private readonly UIButton?[] StatusButtons = new UIButton?[STATUS_COUNT];
    public SocialStatus CurrentStatus { get; private set; }

    public SocialStatusControl()
        : base("lemot")
    {
        Visible = false;
        UsesControlStack = true;
        ZIndex = 2;

        //description label from prefab
        DescriptionLabel = CreateLabel("Description", HorizontalAlignment.Center);

        //wire emot buttons from the prefab — set hover and selected textures from prefab images
        var cache = UiRenderer.Instance!;

        for (var i = 0; i < STATUS_COUNT; i++)
        {
            var btn = CreateButton($"Emot{i}");

            if (btn is not null)
            {
                btn.HoverTexture = btn.PressedTexture;

                //frame[2] = selected texture (not loaded by createbutton)
                var prefab = PrefabSet[$"Emot{i}"];

                if (prefab.Images.Count > 2)
                    btn.SelectedTexture = cache.GetPrefabTexture(PrefabSet.Name, $"Emot{i}", 2);

                WireButton(btn, i);
                StatusButtons[i] = btn;
            }
        }

        //generate missing buttons (emot2-emot7) using the same layout pattern
        if (StatusButtons[0] is { } first && StatusButtons[1] is { } second)
        {
            var stride = second.X - first.X;

            for (var i = 2; i < STATUS_COUNT; i++)
            {
                if (StatusButtons[i] is not null)
                    continue;

                var frameBase = i * FRAMES_PER_STATUS;

                var btn = new UIButton
                {
                    Name = $"Emot{i}",
                    X = first.X + i * stride,
                    Y = first.Y,
                    Width = first.Width,
                    Height = first.Height,
                    NormalTexture = cache.GetEpfTexture("emot000.epf", frameBase),
                    PressedTexture = cache.GetEpfTexture("emot000.epf", frameBase + 1),
                    HoverTexture = cache.GetEpfTexture("emot000.epf", frameBase + 1),
                    SelectedTexture = cache.GetEpfTexture("emot000.epf", frameBase + 2)
                };

                WireButton(btn, i);
                AddChild(btn);
                StatusButtons[i] = btn;
            }
        }
    }


    public event ClosedHandler? OnClosed;
    public event SocialStatusSelectedHandler? OnStatusSelected;

    public new void Show()
    {
        UpdateSelectedState();
        DescriptionLabel?.Text = UiComponentRepository.GetSocialStatusName(CurrentStatus);
        InputDispatcher.Instance?.PushControl(this);
        Visible = true;
    }

    public override void OnKeyDown(KeyDownEvent e)
    {
        if (e.Key == Keys.Escape)
        {
            InputDispatcher.Instance?.RemoveControl(this);
            Visible = false;
            OnClosed?.Invoke();
            e.Handled = true;
        }
    }

    private void UpdateSelectedState()
    {
        for (var i = 0; i < STATUS_COUNT; i++)
            if (StatusButtons[i] is { } btn)
                btn.IsSelected = i == (int)CurrentStatus;
    }

    private void WireButton(UIButton btn, int index)
    {
        btn.Hovered += _ => DescriptionLabel?.Text = UiComponentRepository.SocialStatusNames[index];
        btn.Unhovered += _ => DescriptionLabel?.Text = UiComponentRepository.GetSocialStatusName(CurrentStatus);

        btn.Clicked += () =>
        {
            CurrentStatus = (SocialStatus)index;
            UpdateSelectedState();
            OnStatusSelected?.Invoke(CurrentStatus);
            InputDispatcher.Instance?.RemoveControl(this);
            Visible = false;
            OnClosed?.Invoke();
        };
    }
}