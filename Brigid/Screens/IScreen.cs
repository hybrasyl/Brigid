#region
using Brigid.Controls.Components;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Brigid.Screens;

/// <summary>
///     Represents a discrete game screen (login, character select, game world, etc.). Screens are managed by
///     <see cref="ScreenManager" /> and receive lifecycle callbacks tied to MonoGame's Game class.
/// </summary>
public interface IScreen : IDisposable
{
    /// <summary>
    ///     Root UI panel for this screen. Contains all screen-level UI elements as children. Used by the debug overlay to
    ///     traverse the element tree.
    /// </summary>
    UIPanel? Root { get; }

    /// <summary>
    ///     Called each frame when this screen is the active (topmost) screen. The SpriteBatch is NOT begun — the screen is
    ///     responsible for its own Begin/End calls, allowing each screen to choose its own sampler state, blend mode, and
    ///     transform matrix.
    /// </summary>
    void Draw(SpriteBatch spriteBatch, GameTime gameTime);

    /// <summary>
    ///     Called once when the screen is first pushed onto the screen stack. Use this to subscribe to events, set up state,
    ///     and allocate non-graphics resources.
    /// </summary>
    void Initialize(ChaosGame game);

    /// <summary>
    ///     Called after Initialize, and whenever the graphics device is recreated. Use this to load textures, create
    ///     SpriteBatch resources, etc.
    /// </summary>
    void LoadContent(GraphicsDevice graphicsDevice);

    /// <summary>
    ///     Called when the screen is removed from the stack. Use this to unsubscribe from events and release resources.
    ///     <see cref="IDisposable.Dispose" /> is called immediately after this.
    /// </summary>
    void UnloadContent();

    /// <summary>
    ///     Called each frame when this screen is the active (topmost) screen.
    /// </summary>
    void Update(GameTime gameTime);
}