namespace Brigid.Systems;

/// <summary>
///     What the world screen must do after an <see cref="ExitFlow.Tick" />.
/// </summary>
public enum ExitStep
{
    /// <summary>Nothing.</summary>
    None,

    /// <summary>The confirmation time ended with no answer: confirm, as OK does.</summary>
    Confirm,

    /// <summary>The grace time ended with no logout, and the player chose Exit Game: close the client.</summary>
    CloseClient
}

/// <summary>
///     The timers of the safe quit (<c>0x0B</c>, BRIG-139) and whether the client closes after it. The world screen sends
///     the packets. <see cref="Begin" /> starts the confirmation: the screen sends the exit query and shows the
///     confirmation. OK, or the end of the confirmation time, is <see cref="Confirm" />: the screen sends the exit confirm,
///     and the grace time starts. <see cref="Cancel" /> stops the confirmation. It needs no packet, because the client
///     does not send the confirm. A logout in the grace time is <see cref="CompleteLogout" />.
///     <para>
///         Exit to Menu and Exit Game send the same packets. Exit Game closes the client when the logout completes or
///         the grace time ends. The confirm is sent before the grace time starts, so the client never closes before the
///         server has the exit request.
///     </para>
///     <para>
///         Use it only on the game thread. The screen records a state change of the connection, which comes on the
///         network thread, and acts on it in its update.
///     </para>
/// </summary>
/// <remarks>
///     Some servers close the connection and send no Redirect. Hybrasyl sends the Redirect after 1200 ms, but it removes
///     the player from its world state first. So a disconnect in the grace time is the logout, not a lost connection.
/// </remarks>
public sealed class ExitFlow
{
    /// <summary>The time the confirmation waits for OK or Cancel before it confirms.</summary>
    public const float CONFIRM_SECONDS = 10f;

    /// <summary>The time after the confirm in which a disconnect counts as the logout.</summary>
    public const float GRACE_SECONDS = 10f;

    private float ConfirmSecondsRemaining;
    private float GraceSecondsRemaining;

    /// <summary>True when the player chose Exit Game: the client closes after the logout.</summary>
    public bool ClosesClient { get; private set; }

    /// <summary>True while the confirmation waits for OK or Cancel.</summary>
    public bool IsConfirming => ConfirmSecondsRemaining > 0f;

    /// <summary>
    ///     The whole seconds before the confirmation confirms by itself, rounded up, for the countdown that the
    ///     confirmation shows. 0 when no confirmation is open.
    /// </summary>
    public int ConfirmSecondsLeft => (int)MathF.Ceiling(MathF.Max(ConfirmSecondsRemaining, 0f));

    /// <summary>True after the confirm, until the logout completes or the grace time ends.</summary>
    public bool IsAwaitingLogout => GraceSecondsRemaining > 0f;

    /// <summary>
    ///     Starts the confirmation, and returns true: the screen sends the exit query. When an exit is already in progress,
    ///     it returns false and the screen sends nothing. Exit Game then still closes the client after the logout.
    /// </summary>
    /// <param name="closeClient">True for Exit Game, false for Exit to Menu.</param>
    public bool Begin(bool closeClient)
    {
        if (IsConfirming || IsAwaitingLogout)
        {
            ClosesClient |= closeClient;

            return false;
        }

        ClosesClient = closeClient;
        ConfirmSecondsRemaining = CONFIRM_SECONDS;

        return true;
    }

    /// <summary>Stops the confirmation. The player stays in the world.</summary>
    public void Cancel()
    {
        ConfirmSecondsRemaining = 0f;
        ClosesClient = false;
    }

    /// <summary>Ends the confirmation and starts the grace time. The screen sends the exit confirm.</summary>
    public void Confirm()
    {
        ConfirmSecondsRemaining = 0f;
        GraceSecondsRemaining = GRACE_SECONDS;
    }

    /// <summary>
    ///     Ends the exit, because the logout completed: the server redirected to the login server, or closed the
    ///     connection in the grace time. Returns true when the client must close. A logout that the client did not ask
    ///     for returns false.
    /// </summary>
    public bool CompleteLogout()
    {
        var close = ClosesClient && IsAwaitingLogout;

        ConfirmSecondsRemaining = 0f;
        GraceSecondsRemaining = 0f;
        ClosesClient = false;

        return close;
    }

    /// <summary>
    ///     Advances the timers by <paramref name="seconds" /> and returns what the screen must do. When the confirmation
    ///     time ends, the screen calls <see cref="Confirm" />.
    /// </summary>
    public ExitStep Tick(float seconds)
    {
        if (ConfirmSecondsRemaining > 0f)
        {
            ConfirmSecondsRemaining -= seconds;

            if (ConfirmSecondsRemaining > 0f)
                return ExitStep.None;

            ConfirmSecondsRemaining = 0f;

            return ExitStep.Confirm;
        }

        if (GraceSecondsRemaining > 0f)
        {
            GraceSecondsRemaining -= seconds;

            if ((GraceSecondsRemaining <= 0f) && ClosesClient)
            {
                ClosesClient = false;

                return ExitStep.CloseClient;
            }
        }

        return ExitStep.None;
    }
}
