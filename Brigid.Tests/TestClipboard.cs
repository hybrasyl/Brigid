#region
using System.Runtime.CompilerServices;
using Brigid.Utilities;
#endregion

namespace Brigid.Tests;

/// <summary>
///     An in-memory clipboard for the tests. Tests run with no SDL video subsystem, so the real clipboard refuses every
///     write, and a test must not change the clipboard of the person who runs it.
/// </summary>
internal sealed class TestClipboard : IClipboardBackend
{
    public string Text { get; set; } = string.Empty;

    /// <summary>When true, every write is refused, as a clipboard with no owner refuses it.</summary>
    public bool RefuseWrites { get; set; }

    public static TestClipboard Instance { get; } = new();

    public string GetText() => Text;

    public bool SetText(string text)
    {
        if (RefuseWrites)
            return false;

        Text = text;

        return true;
    }

    [ModuleInitializer]
    internal static void Install() => Clipboard.Backend = Instance;
}
