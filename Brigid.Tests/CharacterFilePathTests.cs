using Brigid.Data.Repositories;
using Xunit;

namespace Brigid.Tests;

// The client loads the player's portrait from the character folder by the name the server sends, and sends the bytes
// back to the server in the editable profile. Path.Combine follows a rooted name or ".." out of the folder, so a hostile
// server could read any file the user can read. A name for a character-folder file must be a bare file name.
public class CharacterFilePathTests
{
    private static readonly string Folder = Path.Combine(Path.GetTempPath(), "profiles", "server", "Angelique");

    [Theory]
    [InlineData("Angelique.jpg")]
    [InlineData("Angelique")]
    [InlineData("profile.txt")]
    public void CombineBareName_JoinsABareNameToTheFolder(string name)
        => Assert.Equal(Path.Combine(Folder, name), LocalPlayerSettingsRepository.CombineBareName(Folder, name));

    [Theory]
    [InlineData("../../secret")]
    [InlineData("..")]
    [InlineData("/home/user/.ssh/id_rsa")]
    [InlineData("C:\\Users\\user\\file.txt")]
    [InlineData("..\\secret.jpg")]
    [InlineData("")]
    public void CombineBareName_RefusesANameThatLeavesTheFolder(string name)
        => Assert.Null(LocalPlayerSettingsRepository.CombineBareName(Folder, name));
}
