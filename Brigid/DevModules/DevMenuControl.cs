#region
using Brigid.Controls.Components;
using Brigid.Controls.Generic;
using Brigid.Rendering;
#endregion

namespace Brigid.DevModules;

/// <summary>
///     The dev menu (BRIG-29): Shift+F11 opens it while <c>BRIGID_DEV_MODULES</c> is set. Each loaded module has a row with
///     its name and description, and then one row for each of its <see cref="IDevModule.Actions" />. A click on an action
///     row closes the menu and runs the action. The menu is made again at each open, on the root of the active screen, so
///     a screen change disposes it with that root. It has no scroll bar, so it is for a small number of actions.
/// </summary>
internal sealed class DevMenuControl : CenteredModalPanel
{
    private const string PANEL_NAME = "devmenu";
    private const int PANEL_W = 360;
    private const int ROW_H = 18;
    private const int ROW_GAP = 4;
    private const int Z_INDEX = 60;

    internal DevMenuControl(IReadOnlyList<IDevModule> modules, Action<string> log)
        //AddRows sets the height
        : base("Dev modules", PANEL_W, 0)
    {
        Name = PANEL_NAME;
        AddRows(modules, log);
        OnClose += Remove;
    }

    /// <summary>
    ///     Opens the menu on <paramref name="root" />, or closes it when it is open there. Does nothing with no root (the
    ///     launcher screen). The control stack takes the focus from a text box while the menu is open and gives it back
    ///     when the menu closes.
    /// </summary>
    internal static void Toggle(UIPanel? root, IReadOnlyList<IDevModule> modules, Action<string> log)
    {
        if (root is null)
            return;

        if (root.FindChild<DevMenuControl>(PANEL_NAME) is { } open)
        {
            open.Hide();

            return;
        }

        var menu = new DevMenuControl(modules, log) { ZIndex = Z_INDEX };
        root.AddChild(menu);
        menu.Show();
    }

    private void AddRows(IReadOnlyList<IDevModule> modules, Action<string> log)
    {
        var content = ContentBounds;
        var y = content.Y;

        if (modules.Count == 0)
        {
            AddText("empty", "No dev module is loaded.", content.X, y, content.Width);
            y += ROW_H;
        }

        for (var i = 0; i < modules.Count; i++)
        {
            var module = modules[i];
            var name = module.GetType().Name;
            var description = string.Empty;
            IReadOnlyList<DevAction>? actions = null;
            DevModuleLoader.Guard(log, $"{name}: Name, Description and Actions", () => (name, description, actions) = (module.Name, module.Description, module.Actions));

            AddText($"module_{i}", string.IsNullOrWhiteSpace(description) ? name : $"{name}: {description}", content.X, y, content.Width);
            y += ROW_H;

            var rows = (actions ?? []).Where(action => action is not null).ToList();

            for (var j = 0; j < rows.Count; j++)
            {
                var action = rows[j];
                var label = action.Label ?? string.Empty;
                var row = new TextButton(label, content.Width, ROW_H)
                {
                    Name = $"action_{i}_{j}",
                    X = content.X,
                    Y = y
                };
                var what = $"{name}: {label}";
                row.Clicked += () =>
                {
                    Hide();
                    DevModuleLoader.Guard(log, what, () => action.Invoke());
                };
                AddChild(row);
                y += ROW_H + ROW_GAP;
            }
        }

        SetContentHeight(y - content.Y);
    }

    private void AddText(string name, string text, int x, int y, int width)
    {
        var label = new UILabel
        {
            Name = name,
            X = x,
            Y = y + (ROW_H - TextRenderer.CHAR_HEIGHT) / 2,
            Width = width,
            Height = TextRenderer.CHAR_HEIGHT,
            Text = text,
            IsHitTestVisible = false
        };
        label.SetForegroundRole(ThemeRoles.Text);
        AddChild(label);
    }

    //the menu is made again at each open, so a close removes it from its parent
    private void Remove() => Parent?.RemoveChild(PANEL_NAME);
}
