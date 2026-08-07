using Brigid.Networking;
using Xunit;

namespace Brigid.Tests;

/// <summary>
///     Classification of the 0x02 LoginMessage result codes during character creation.
/// </summary>
/// <remarks>
///     The regression is 0x04: USDA rejects a taken or reserved name with it, Chaos's
///     <c>LoginMessageType</c> has no member for it, and the handler used to throw
///     <c>ArgumentOutOfRangeException</c> out of the packet loop on receipt. Retail treats 4
///     exactly as 3.
/// </remarks>
public class CharCreateFailureTests
{
    /// <summary>The captured USDA rejection — 0x04 with "That name already exists…".</summary>
    [Fact]
    public void NameAlreadyExists_BlamesTheNameField()
        => Assert.Equal(CharCreateFailureField.Name, CharCreateFailure.FieldFor(0x04));

    /// <summary>Retail plays sound 9 for 3 and 4 alike, which is what groups them.</summary>
    [Theory]
    [InlineData(0x03)]
    [InlineData(0x04)]
    public void NameErrors_BlameTheNameField(byte type)
        => Assert.Equal(CharCreateFailureField.Name, CharCreateFailure.FieldFor(type));

    /// <summary>Retail plays sounds 10 + 0x0B across the whole 5–10 block.</summary>
    [Theory]
    [InlineData(0x05)]
    [InlineData(0x06)]
    [InlineData(0x07)]
    [InlineData(0x08)]
    [InlineData(0x09)]
    [InlineData(0x0A)]
    public void PasswordErrors_BlameThePasswordField(byte type)
        => Assert.Equal(CharCreateFailureField.Password, CharCreateFailure.FieldFor(type));

    /// <summary>
    ///     0x0B is message-only on retail, and success never reaches the classifier. The rest are
    ///     undocumented; none of them may throw, which is the defect this guards.
    /// </summary>
    [Theory]
    [InlineData(0x00)]
    [InlineData(0x01)]
    [InlineData(0x02)]
    [InlineData(0x0B)]
    [InlineData(0x0E)]
    [InlineData(0x0F)]
    [InlineData(0x50)]
    [InlineData(0xFF)]
    public void UnclassifiedCodes_BlameNoField(byte type)
        => Assert.Equal(CharCreateFailureField.None, CharCreateFailure.FieldFor(type));

    /// <summary>
    ///     Retail defines 0, the 3–10 error families, and the message-only 0x0B. The caller logs anything
    ///     else, which is the diagnostic the removed throw was accidentally providing.
    /// </summary>
    [Theory]
    [InlineData(0x00)]
    [InlineData(0x03)]
    [InlineData(0x04)]
    [InlineData(0x05)]
    [InlineData(0x0A)]
    [InlineData(0x0B)]
    public void DocumentedCodes_AreNotReportedAsUnknown(byte type)
        => Assert.True(CharCreateFailure.IsDocumented(type));

    /// <summary>
    ///     0x01/0x02 sit inside retail's range but have no defined behaviour, and 0x0E/0x0F are login-screen
    ///     codes with no meaning during creation — all four are undocumented *here*.
    /// </summary>
    [Theory]
    [InlineData(0x01)]
    [InlineData(0x02)]
    [InlineData(0x0C)]
    [InlineData(0x0E)]
    [InlineData(0x0F)]
    [InlineData(0xFF)]
    public void UndocumentedCodes_AreReportedAsUnknown(byte type)
        => Assert.False(CharCreateFailure.IsDocumented(type));

    /// <summary>
    ///     0x0B classifies as None because it blames no field, not because it is unknown — the two reasons a
    ///     code reaches None must stay distinguishable, which is the whole point of the predicate.
    /// </summary>
    [Fact]
    public void MessageOnlyIsNotConflatedWithUnknown()
    {
        Assert.Equal(CharCreateFailureField.None, CharCreateFailure.FieldFor(0x0B));
        Assert.Equal(CharCreateFailureField.None, CharCreateFailure.FieldFor(0xFF));

        Assert.True(CharCreateFailure.IsDocumented(0x0B));
        Assert.False(CharCreateFailure.IsDocumented(0xFF));
    }

    /// <summary>No code may be both, and none may throw — swept over the whole byte range.</summary>
    [Fact]
    public void EveryByteClassifiesWithoutThrowing()
    {
        for (var type = 0; type <= byte.MaxValue; type++)
        {
            var field = CharCreateFailure.FieldFor((byte)type);

            Assert.True(field is CharCreateFailureField.None or CharCreateFailureField.Name or CharCreateFailureField.Password);
        }
    }
}
