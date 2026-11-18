namespace Brigid.Data.Models;

/// <summary>The experience at which a level started, for one level (BRIG-148).</summary>
public readonly record struct SavedLevelStart(byte Level, ulong Start);

/// <summary>
///     The starts of the current level and the current ability level that the client learned for a character (BRIG-148),
///     saved in Experience.cfg so that the experience bar is correct at the next login. Each is null when it is not known.
/// </summary>
public sealed record SavedExperience(SavedLevelStart? Level, SavedLevelStart? Ability);
