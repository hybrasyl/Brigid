#region
using Brigid.ViewModel;
#endregion

namespace Brigid.Controls.World.Hud.Main;

/// <summary>What the experience bar shows: the fill, its colour, and its tooltip.</summary>
/// <param name="Fraction">The filled part of the bar, from 0 to 1.</param>
/// <param name="UsesCapFill">True when the bar shows the cap fill (blue) in place of the experience fill (yellow).</param>
/// <param name="Title">The title of the tooltip: the level.</param>
/// <param name="Lines">The lines of the tooltip under the title.</param>
internal readonly record struct ExperienceView(float Fraction, bool UsesCapFill, string Title, IReadOnlyList<string> Lines);

/// <summary>
///     The rules of the experience bar of the new HUD (BRIG-148, BRIG-127 D27). Below the level cap, the bar shows the
///     experience of the current level, while the start of the level is known (<see cref="ExperienceSpan" />). At the level
///     cap the server sends 0 experience to the next level:
///     <list type="bullet">
///         <item>
///             On a server that converts the experience past the cap into Crystallized Reflections (Hybrasyl), the bar is
///             full and blue, and the tooltip shows the total experience. The server owns the conversion rate, which can
///             change for each character, and the total does not show the progress toward the next Reflection; the bar
///             shows that progress when the server sends it in the <c>0x0008</c> Experience block (HS-1722).
///         </item>
///         <item>
///             On retail, the bar is full and yellow while the ability level is 0. With an ability level, it is blue and
///             shows the ability experience of the current ability level, and it is full at the ability cap. The tooltip
///             shows the ability experience in both states.
///         </item>
///     </list>
/// </summary>
internal static class HudExperience
{
    /// <summary>
    ///     The fill of the bar and its colour, with no tooltip lines: an attributes packet comes at each change of the
    ///     vitals, and the HUD reads the lines only while the tooltip shows.
    /// </summary>
    public static (float Fraction, bool UsesCapFill) Fill(PlayerAttributes attrs, bool convertsPastCap)
    {
        var view = Build(attrs, convertsPastCap, null);

        return (view.Fraction, view.UsesCapFill);
    }

    /// <summary>The fill, its colour and the tooltip.</summary>
    public static ExperienceView Describe(PlayerAttributes attrs, bool convertsPastCap)
        => Build(attrs, convertsPastCap, []);

    //lines is null for the fill only: a null list adds nothing, and the text of a line is not made
    private static ExperienceView Build(PlayerAttributes attrs, bool convertsPastCap, List<string>? lines)
    {
        var title = lines is null ? string.Empty : HudFormat.Level(attrs.Level);
        IReadOnlyList<string> shown = lines ?? [];

        if (attrs.ToNextLevel > 0)
        {
            var progress = attrs.LevelSpan.Progress(attrs.Level, attrs.TotalExp, attrs.ToNextLevel);
            lines?.Add(ProgressLine(progress, attrs.ToNextLevel, $"level {attrs.Level + 1}"));
            lines?.Add($"Total: {HudFormat.Number(attrs.TotalExp)}");

            return new ExperienceView(FractionOf(progress), false, title, shown);
        }

        lines?.Add($"Total: {HudFormat.Number(attrs.TotalExp)}");

        if (convertsPastCap)
            return new ExperienceView(1, true, title, shown);

        lines?.Add($"Ability level {attrs.Ability}");
        lines?.Add($"Ability experience: {HudFormat.Number(attrs.TotalAbility)}");

        if (attrs.Ability == 0)
            return new ExperienceView(1, false, title, shown);

        if (attrs.ToNextAbility == 0)
            return new ExperienceView(1, true, title, shown);

        var ability = attrs.AbilitySpan.Progress(attrs.Ability, attrs.TotalAbility, attrs.ToNextAbility);
        lines?.Add(ProgressLine(ability, attrs.ToNextAbility, $"ability level {attrs.Ability + 1}"));

        return new ExperienceView(FractionOf(ability), true, title, shown);
    }

    //the gained experience of the full level, else the experience to the next level
    private static string ProgressLine((ulong Gained, ulong Full)? progress, uint toNext, string next)
        => progress is { } known
            ? $"{HudFormat.Number(known.Gained)} / {HudFormat.Number(known.Full)} ({Percent(known)}%)"
            : $"{HudFormat.Number(toNext)} to {next}";

    private static float FractionOf((ulong Gained, ulong Full)? progress)
        => progress is { Full: > 0 } known ? (float)((double)known.Gained / known.Full) : 0;

    //the percent rounds down, so the tooltip shows 100% only for a full level
    private static ulong Percent((ulong Gained, ulong Full) progress) => progress.Gained * 100 / progress.Full;

}
