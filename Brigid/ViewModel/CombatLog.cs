namespace Brigid.ViewModel;

/// <summary>
///     Finds the lines of Hybrasyl's combat log. While the player has turned the log on (<c>/combatlog</c>), the server
///     sends each line of each combat event as a group message (<c>User.SendCombatLogMessage</c>). Every first line
///     starts with <c>[combat] </c>; the second line of a damage event starts with two spaces and <c>[Applied]</c> or
///     <c>[Immune]</c>; a refused loot starts with <c>[LOOT DENIED]</c>; a loot event ends with an empty line. The match
///     is exact, because a group message of a player always starts with <c>[!Name] </c>. Retail has no combat log.
/// </summary>
public static class CombatLog
{
    private static readonly string[] Prefixes = ["[combat] ", "[LOOT DENIED]", "  [Applied]", "  [Immune]"];

    /// <summary>True when the text of a group message is a line of the combat log.</summary>
    public static bool IsCombatLine(string text)
    {
        if (text.Length == 0)
            return true;

        foreach (var prefix in Prefixes)
            if (text.StartsWith(prefix, StringComparison.Ordinal))
                return true;

        return false;
    }
}
