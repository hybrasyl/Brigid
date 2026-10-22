#region
using Brigid.Controls.Components;
using Brigid.Controls.World.Popups.Profile;
using SkiaSharp;
using Xunit;
#endregion

namespace Brigid.Tests;

/// <summary>
///     BRIG-106: the self profile shows the player's own portrait from the local character folder, decoded as the other
///     player's portrait is. Empty data, or data that does not decode, shows no portrait.
/// </summary>
public class ProfilePortraitTests
{
    private static byte[] Jpeg()
    {
        using var bitmap = new SKBitmap(4, 4);
        bitmap.Erase(SKColors.Teal);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, 90);

        return data.ToArray();
    }

    [Fact]
    public void APortraitFileDecodes()
    {
        using var decoded = ProfilePortrait.Decode(Jpeg());

        Assert.NotNull(decoded);
        Assert.Equal(4, decoded.Width);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(new byte[0])]
    [InlineData(new byte[] { 1, 2, 3, 4 })]
    public void NoFileOrABadFileGivesNoPortrait(byte[]? data)
    {
        var image = new UIImage();

        Assert.Null(ProfilePortrait.Decode(data));

        ProfilePortrait.Show(image, data);

        Assert.Null(image.Texture);
    }
}
