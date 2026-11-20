#region
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Brigid.Screens;

/// <summary>
///     Manages a stack of game screens. The topmost screen receives Update and Draw calls. Supports Push (overlay), Pop
///     (return to previous), and Switch (replace entire stack).
/// </summary>
public sealed class ScreenManager : IDisposable
{
    private readonly ChaosGame Game;
    private readonly Stack<IScreen> Screens = new();

    /// <summary>
    ///     The currently active screen (top of the stack), or null if the stack is empty.
    /// </summary>
    public IScreen? ActiveScreen => Screens.Count > 0 ? Screens.Peek() : null;

    /// <summary>
    ///     Raised after the active screen changes: after <see cref="Push" /> (also through <see cref="Switch" />, once) and
    ///     after <see cref="Pop" />. The new screen has its <see cref="IScreen.Root" /> by then. The argument is the new
    ///     active screen, or null when the stack is empty. A panel that a listener added to the old root is disposed with it.
    ///     Internal: the dev module host is the one listener, and it guards each module listener (BRIG-29).
    /// </summary>
    internal event Action<IScreen?>? ScreenChanged;

    public ScreenManager(ChaosGame game) => Game = game;

    /// <inheritdoc />
    public void Dispose()
    {
        while (Screens.Count > 0)
            RemoveTop();
    }

    /// <summary>
    ///     Delegates the Draw call to the active screen.
    /// </summary>
    public void Draw(SpriteBatch spriteBatch, GameTime gameTime) => ActiveScreen?.Draw(spriteBatch, gameTime);

    /// <summary>
    ///     Delegates the native-resolution draw (overlays + UI) to the active screen.
    /// </summary>
    public void DrawNative(SpriteBatch spriteBatch, float scaleX, float scaleY)
        => ActiveScreen?.DrawNative(spriteBatch, scaleX, scaleY);

    /// <summary>
    ///     Removes and disposes the topmost screen, returning control to the screen below it. Does nothing if the stack is
    ///     empty.
    /// </summary>
    public void Pop()
    {
        if (Screens.Count == 0)
            return;

        RemoveTop();
        ScreenChanged?.Invoke(ActiveScreen);
    }

    /// <summary>
    ///     Pushes a screen onto the top of the stack, making it the active screen. The previous screen remains in the stack
    ///     and will resume when this one is popped.
    /// </summary>
    public void Push(IScreen screen)
    {
        Screens.Push(screen);
        screen.Initialize(Game);
        screen.LoadContent(Game.GraphicsDevice);
        ScreenChanged?.Invoke(screen);
    }

    private void RemoveTop()
    {
        var screen = Screens.Pop();
        screen.UnloadContent();
        screen.Dispose();
    }

    /// <summary>
    ///     Replaces the entire screen stack with a single new screen. All existing screens are unloaded and disposed.
    /// </summary>
    public void Switch(IScreen screen)
    {
        while (Screens.Count > 0)
            RemoveTop();

        Game.Dispatcher.Clear();
        Push(screen);
    }

    /// <summary>
    ///     Delegates the Update call to the active screen.
    /// </summary>
    public void Update(GameTime gameTime) => ActiveScreen?.Update(gameTime);
}