namespace Brigid.Networking;

/// <summary>
///     The character-creation field a 0x02 LoginMessage failure code blames.
/// </summary>
internal enum CharCreateFailureField
{
    /// <summary>The code carries a message only; no field is at fault.</summary>
    None,

    /// <summary>The requested name was rejected.</summary>
    Name,

    /// <summary>The requested password was rejected.</summary>
    Password
}

/// <summary>
///     Classifies the 0x02 LoginMessage result codes a server can return to a pending CreateA.
/// </summary>
/// <remarks>
///     <para>
///         The retail client's CreateA-pending handler groups the codes by the sound it plays,
///         which is what separates the two families:
///     </para>
///     <code>
///         0                     success — auto-fire CreateB; message not displayed
///         3, 4                  reset state, sound 9, display server message
///         5, 6, 7, 8, 9, 10     reset state, sounds 10 + 0x0B, display server message
///         0x0B                  reset state, display server message (no sound)
///         any other             no-op
///     </code>
///     <para>
///         Ghidra-verified at <c>FUN_0043e360</c>; Comhaigne
///         <c>docs/protocol/server/0x02-login-message.md</c> @
///         <c>023d886130b547e903b9ee42e977859075f91d70</c>. The 5–10 family is password errors per
///         the semantic assignments recorded there (0x05 length, 0x07 numeric portion too short,
///         0x08 too simple, 0x09 invalid characters).
///     </para>
///     <para>
///         Chaos's <c>LoginMessageType</c> names only 0, 3, 5, 14 and 15. USDA rejects a taken or
///         reserved name with 4 — a code that enum cannot represent, so the classification is done
///         on the raw byte.
///     </para>
///     <para>
///         Codes outside the documented set map to <see cref="CharCreateFailureField.None" />
///         rather than being ignored, so the caller still surfaces the server's message and
///         releases the form. Retail drops them, leaving its form stuck pending; no conformant
///         server emits them.
///     </para>
/// </remarks>
internal static class CharCreateFailure
{
    /// <summary>
    ///     Maps a 0x02 LoginMessage result code to the field the client should clear.
    /// </summary>
    /// <param name="type">The result code byte, straight off the wire.</param>
    public static CharCreateFailureField FieldFor(byte type)
        => type switch
        {
            >= 0x03 and <= 0x04 => CharCreateFailureField.Name,
            >= 0x05 and <= 0x0A => CharCreateFailureField.Password,
            _                   => CharCreateFailureField.None
        };
}
