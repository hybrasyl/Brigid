#region
using Brigid.ViewModel;
using Xunit;
#endregion

namespace Brigid.Tests;

/// <summary>
///     Guards the board session lifecycle the modal-exclusivity guard relies on: <see cref="Board.SessionOpened" />
///     must fire only on the closed→open transition, so a board-list refresh while already open does not re-close the
///     other toggle panels, while a fresh open (by any route) still does.
/// </summary>
public sealed class BoardTests
{
    [Fact]
    public void OpenSession_FiresSessionOpenedOncePerTransition()
    {
        var board = new Board();
        var opened = 0;
        board.SessionOpened += () => opened++;

        board.OpenSession();
        board.OpenSession(); //already open — a board-list refresh must not re-fire

        Assert.Equal(1, opened);
        Assert.True(board.IsSessionOpen);
    }

    [Fact]
    public void CloseThenOpen_FiresSessionOpenedAgain()
    {
        var board = new Board();
        var opened = 0;
        board.SessionOpened += () => opened++;

        board.OpenSession();
        board.CloseSession();
        board.OpenSession();

        Assert.Equal(2, opened);
        Assert.True(board.IsSessionOpen);
    }
}
