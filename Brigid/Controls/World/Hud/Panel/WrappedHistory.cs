#region
using Brigid.Rendering;
using Brigid.ViewModel;
#endregion

namespace Brigid.Controls.World.Hud.Panel;

/// <summary>
///     The orange-bar history wrapped into display lines for <see cref="SystemMessagePanel" />, oldest first. Each line
///     keeps the color of its message, and an empty message is one empty line. A new message wraps only itself
///     (<see cref="Append" />); the lines of the messages that the full history buffer dropped go from the start. A clear,
///     a new face or a new width wraps all of it again (<see cref="Rebuild" />).
/// </summary>
internal sealed class WrappedHistory
{
    //the number of lines of each message in Lines, in history order
    private readonly List<int> LineCounts = [];
    private readonly List<Chat.OrangeBarMessage> WrappedLines = [];

    internal IReadOnlyList<Chat.OrangeBarMessage> Lines => WrappedLines;

    /// <summary>Wraps all of <paramref name="history" /> at <paramref name="width" />.</summary>
    internal void Rebuild(IReadOnlyList<Chat.OrangeBarMessage> history, int width)
    {
        LineCounts.Clear();
        WrappedLines.Clear();

        for (var i = 0; i < history.Count; i++)
            Add(history[i], width);
    }

    /// <summary>
    ///     Wraps the last <paramref name="added" /> messages of <paramref name="history" />, and removes the lines of the
    ///     messages that left the start of the history. More messages than the history holds wraps all of it.
    /// </summary>
    internal void Append(IReadOnlyList<Chat.OrangeBarMessage> history, int added, int width)
    {
        var dropped = LineCounts.Count + added - history.Count;

        if ((added > history.Count) || (dropped < 0) || (dropped > LineCounts.Count))
        {
            Rebuild(history, width);

            return;
        }

        WrappedLines.RemoveRange(0, LineCounts.Take(dropped).Sum());
        LineCounts.RemoveRange(0, dropped);

        for (var i = history.Count - added; i < history.Count; i++)
            Add(history[i], width);
    }

    private void Add(Chat.OrangeBarMessage message, int width)
    {
        var wrapped = TextRenderer.WrapLines(message.Text, width);

        if (wrapped.Count == 0)
            WrappedLines.Add(message with { Text = string.Empty });
        else
            WrappedLines.AddRange(wrapped.Select(line => message with { Text = line }));

        LineCounts.Add(Math.Max(1, wrapped.Count));
    }
}
