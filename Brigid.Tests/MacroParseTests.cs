using Brigid.Data.Repositories;
using Xunit;

namespace Brigid.Tests;

// Initialize writes an empty Macro.cfg for a new character. The Options macro boxes take the parsed values as their
// text, and a null there crashed the client on the next update. Every slot must be a string, also with no lines.
public class MacroParseTests
{
    [Fact]
    public void ParseMacros_EmptyFile_GivesTenEmptySlots()
    {
        var macros = LocalPlayerSettingsRepository.ParseMacros([]);

        Assert.Equal(10, macros.Length);
        Assert.All(macros, m => Assert.Equal(string.Empty, m));
    }

    [Fact]
    public void ParseMacros_ShortFile_FillsTheRestWithEmptySlots()
    {
        var macros = LocalPlayerSettingsRepository.ParseMacros(["macro0: \"hello\"", "macro1: world"]);

        Assert.Equal("hello", macros[0]);
        Assert.Equal("world", macros[1]);
        Assert.All(macros[2..], m => Assert.Equal(string.Empty, m));
    }

    [Fact]
    public void FormatThenParse_ReturnsTheSameMacros()
    {
        string[] macros =
        [
            "hello",
            string.Empty,
            "/say time: now",
            "say \"hi\"",
            "  padded  ",
            "x",
            string.Empty,
            "a:b:c",
            "\"",
            "last"
        ];

        Assert.Equal(macros, LocalPlayerSettingsRepository.ParseMacros(LocalPlayerSettingsRepository.FormatMacros(macros)));
    }
}
