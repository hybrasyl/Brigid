#region
using Brigid.Controls.Generic;
using Xunit;
#endregion

namespace Brigid.Tests;

/// <summary>
///     The fingerprint layout in the trust prompt. The whole point of showing a fingerprint is that a
///     user can compare it against a value the operator published, so where the line breaks is
///     load-bearing rather than cosmetic.
/// </summary>
public class CertificateTrustPromptTests
{
    private static string Sha256(int bytes)
        => string.Join(':', Enumerable.Range(0, bytes)
                                      .Select(i => i.ToString("X2")));

    /// <summary>A real SHA-256 splits into two equal, byte-aligned halves.</summary>
    [Fact]
    public void Sha256_SplitsIntoTwoByteAlignedHalves()
    {
        var (first, second) = CertificateTrustPromptControl.SplitFingerprint(Sha256(32));

        Assert.Equal(16, first.Split(':').Length);
        Assert.Equal(16, second.Split(':').Length);

        //recombining must reproduce the original exactly — a split that drops or duplicates a byte
        //would still look plausible on screen.
        Assert.Equal(Sha256(32), $"{first}:{second}");
    }

    /// <summary>
    ///     Every byte on both lines is a full two-character pair. A break landing mid-byte yields a
    ///     fingerprint that cannot be checked against a published one, which is worse than not showing it.
    /// </summary>
    [Fact]
    public void SplitLines_ContainNoPartialBytes()
    {
        var (first, second) = CertificateTrustPromptControl.SplitFingerprint(Sha256(32));

        Assert.All(first.Split(':'), part => Assert.Equal(2, part.Length));
        Assert.All(second.Split(':'), part => Assert.Equal(2, part.Length));
    }

    /// <summary>A short fingerprint stays on one line rather than being padded onto two.</summary>
    [Fact]
    public void ShortFingerprint_StaysOnOneLine()
    {
        var (first, second) = CertificateTrustPromptControl.SplitFingerprint("AA:BB:CC");

        Assert.Equal("AA:BB:CC", first);
        Assert.Empty(second);
    }

    /// <summary>Absent or empty input must not throw while a prompt is being built.</summary>
    [Fact]
    public void EmptyFingerprint_IsHandled()
    {
        var (first, second) = CertificateTrustPromptControl.SplitFingerprint(string.Empty);

        Assert.Empty(first);
        Assert.Empty(second);
    }
}
