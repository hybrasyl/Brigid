using Brigid.Systems;
using Xunit;

namespace Brigid.Tests;

/// <summary>
///     BRIG-139: Exit to Menu and Exit Game share the safe quit. The confirmation confirms by itself after its time, Cancel
///     stops it, and Exit Game closes the client when the logout completes or when the grace time after the confirm ends.
/// </summary>
public sealed class ExitFlowTests
{
    private readonly ExitFlow Flow = new();

    [Fact]
    public void TheConfirmationConfirmsByItselfWhenItsTimeEnds()
    {
        Assert.True(Flow.Begin(false));

        Assert.Equal(ExitStep.None, Flow.Tick(ExitFlow.CONFIRM_SECONDS - 1));
        Assert.True(Flow.IsConfirming);
        Assert.Equal(ExitStep.Confirm, Flow.Tick(1));
        Assert.False(Flow.IsConfirming);

        //the screen confirms: it sends the exit confirm, and the grace time starts
        Flow.Confirm();

        Assert.True(Flow.IsAwaitingLogout);
    }

    [Fact]
    public void TheCountdownRoundsUpToWholeSeconds()
    {
        Assert.Equal(0, Flow.ConfirmSecondsLeft);

        Flow.Begin(false);

        Assert.Equal((int)ExitFlow.CONFIRM_SECONDS, Flow.ConfirmSecondsLeft);

        Flow.Tick(0.25f);

        Assert.Equal((int)ExitFlow.CONFIRM_SECONDS, Flow.ConfirmSecondsLeft);

        //0.75 s left
        Flow.Tick(ExitFlow.CONFIRM_SECONDS - 1f);

        Assert.Equal(1, Flow.ConfirmSecondsLeft);

        Flow.Cancel();

        Assert.Equal(0, Flow.ConfirmSecondsLeft);
    }

    //a long frame ends the confirmation only; the grace time starts with the screen's confirm, not in the same tick
    [Fact]
    public void ALongTickConfirmsAndDoesNotClose()
    {
        Flow.Begin(true);

        Assert.Equal(ExitStep.Confirm, Flow.Tick(ExitFlow.CONFIRM_SECONDS + ExitFlow.GRACE_SECONDS + 5));
        Assert.Equal(ExitStep.None, Flow.Tick(1));
        Assert.True(Flow.ClosesClient);
    }

    //the log-out Escape fault: a cancel stops the timer, so no logout comes after it
    [Fact]
    public void CancelStopsTheConfirmation_AndNoLogoutFollows()
    {
        Flow.Begin(true);

        Flow.Cancel();

        Assert.False(Flow.IsConfirming);
        Assert.False(Flow.ClosesClient);
        Assert.Equal(ExitStep.None, Flow.Tick(ExitFlow.CONFIRM_SECONDS + ExitFlow.GRACE_SECONDS));
        Assert.False(Flow.IsAwaitingLogout);
    }

    [Fact]
    public void ASecondExitToMenuWhileConfirmingSendsNothing()
    {
        Assert.True(Flow.Begin(false));
        Assert.False(Flow.Begin(false));

        Assert.True(Flow.IsConfirming);
        Assert.False(Flow.ClosesClient);
    }

    //the screen then shows the Exit Game confirmation
    [Fact]
    public void ExitGameWhileTheLogOutConfirmationIsOpenChangesItToExitGame()
    {
        Flow.Begin(false);

        Assert.False(Flow.Begin(true));
        Assert.True(Flow.ClosesClient);

        Flow.Confirm();

        Assert.True(Flow.CompleteLogout());
    }

    [Fact]
    public void AfterACancelTheNextBeginStartsAgain()
    {
        Flow.Begin(true);
        Flow.Cancel();

        Assert.True(Flow.Begin(false));
        Assert.False(Flow.ClosesClient);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void TheLogoutClosesTheClientOnlyForExitGame(bool closeClient, bool expectedClose)
    {
        Flow.Begin(closeClient);
        Flow.Confirm();

        Assert.Equal(expectedClose, Flow.CompleteLogout());
        Assert.False(Flow.IsAwaitingLogout);
    }

    [Fact]
    public void ExitGameClosesTheClientWhenTheGraceTimeEndsWithNoLogout()
    {
        Flow.Begin(true);
        Flow.Confirm();

        Assert.Equal(ExitStep.None, Flow.Tick(ExitFlow.GRACE_SECONDS - 1));
        Assert.Equal(ExitStep.CloseClient, Flow.Tick(1));
        Assert.Equal(ExitStep.None, Flow.Tick(1));
    }

    //a slow logout redirect: its result ends the exit, not the grace time
    [Fact]
    public void TheGraceTimeDoesNotRunWhileARedirectConnects()
    {
        Flow.Begin(false);
        Flow.Confirm();

        Assert.Equal(ExitStep.None, Flow.Tick(ExitFlow.GRACE_SECONDS * 3, redirecting: true));
        Assert.True(Flow.IsAwaitingLogout);
        Assert.Equal(ExitStep.ExitIgnored, Flow.Tick(ExitFlow.GRACE_SECONDS));
    }

    [Fact]
    public void ExitToMenuLeavesThePlayerInTheWorldWhenTheGraceTimeEnds()
    {
        Flow.Begin(false);
        Flow.Confirm();

        //BRIG-169: the screen then tells the connection to forget the confirm, once
        Assert.Equal(ExitStep.None, Flow.Tick(ExitFlow.GRACE_SECONDS - 1));
        Assert.Equal(ExitStep.ExitIgnored, Flow.Tick(1));
        Assert.Equal(ExitStep.None, Flow.Tick(1));
        Assert.False(Flow.IsAwaitingLogout);
    }

    [Fact]
    public void ExitGameDuringTheGraceTimeOfExitToMenuClosesTheClient()
    {
        Flow.Begin(false);
        Flow.Confirm();

        Assert.False(Flow.Begin(true));
        Assert.Equal(ExitStep.CloseClient, Flow.Tick(ExitFlow.GRACE_SECONDS));
    }

    //the last choice wins: the player who changes their mind to Exit to Menu stays in the client
    [Fact]
    public void ExitToMenuWhileTheExitGameConfirmationIsOpenChangesItToExitToMenu()
    {
        Flow.Begin(true);

        Assert.False(Flow.Begin(false));
        Assert.False(Flow.ClosesClient);

        Flow.Confirm();

        Assert.False(Flow.CompleteLogout());
    }

    [Fact]
    public void ExitToMenuDuringTheGraceTimeOfExitGameDoesNotCloseTheClient()
    {
        Flow.Begin(true);
        Flow.Confirm();

        Assert.False(Flow.Begin(false));
        Assert.Equal(ExitStep.ExitIgnored, Flow.Tick(ExitFlow.GRACE_SECONDS));
        Assert.False(Flow.IsAwaitingLogout);
    }

    [Fact]
    public void ALogoutWithNoExitInProgressDoesNotClose()
    {
        Assert.False(Flow.CompleteLogout());
    }

    //the logout came during the confirmation, before the client sent the confirm: the server logged the player out
    [Fact]
    public void ALogoutDuringTheConfirmationDoesNotClose()
    {
        Flow.Begin(true);

        Assert.False(Flow.CompleteLogout());
        Assert.False(Flow.IsConfirming);
    }

    [Fact]
    public void ALogoutAfterTheGraceTimeDoesNotClose()
    {
        Flow.Begin(true);
        Flow.Confirm();
        Flow.Tick(ExitFlow.GRACE_SECONDS);

        Assert.False(Flow.CompleteLogout());
    }
}
