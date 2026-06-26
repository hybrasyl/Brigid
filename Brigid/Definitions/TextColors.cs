#region
using Microsoft.Xna.Framework;
#endregion

namespace Brigid.Definitions;

/// <summary>
///     Semantic text color aliases derived from <see cref="LegendColors" />. Must be initialized after
///     <see cref="LegendColors" />.
/// </summary>
public static class TextColors
{
    public static Color Default { get; private set; }
    public static Color GroupChat { get; private set; }
    public static Color GuildChat { get; private set; }
    public static Color Shout { get; private set; }
    public static Color Whisper { get; private set; }

    public static void Initialize()
    {
        Default = LegendColors.Silver;
        Shout = LegendColors.CanaryYellow;
        Whisper = LegendColors.CornflowerBlue;
        GroupChat = LegendColors.Olive;
        GuildChat = LegendColors.Seafoam;
    }
}