#region
using DALib.Data;
#endregion

namespace Brigid.Data.Models;

/// <summary>
///     A single parsed event/quest from an SEvent metadata file.
/// </summary>
public sealed record EventMetadataEntry
{
    public string Id { get; init; } = string.Empty;

    /// <summary>
    ///     The page this event belongs to (1-based, corresponds to SEvent file number / circle level).
    /// </summary>
    public int Page { get; init; } = 1;

    public string PreRequisiteId { get; init; } = string.Empty;

    /// <summary>
    ///     Digit string of qualifying circle numbers (e.g. "1234567"). Each char is a LevelCircle int value.
    /// </summary>
    public string QualifyingCircles { get; init; } = string.Empty;

    /// <summary>
    ///     Digit string of qualifying class numbers (e.g. "012345"). Each char is a BaseClass int value.
    /// </summary>
    public string QualifyingClasses { get; init; } = string.Empty;

    public string Result { get; init; } = string.Empty;
    public string Reward { get; init; } = string.Empty;

    public string Summary { get; init; } = string.Empty;
    public required string Title { get; init; }

    /// <summary>
    ///     Parses all event entries from one or more SEvent MetaFiles.
    /// </summary>
    /// <remarks>
    ///     Each event is encoded as 9 sequential sub-nodes: {page}_start, _title, _id, _qual, _sum, _result, _sub, _reward,
    ///     _end.
    /// </remarks>
    public static IReadOnlyList<EventMetadataEntry> ParseAll(IEnumerable<MetaFile> metaFiles)
    {
        var events = new List<EventMetadataEntry>();

        foreach (var metaFile in metaFiles)
        {
            var currentPage = 1;
            string? currentTitle = null;
            string? currentId = null;
            string? qualCircles = null;
            string? qualClasses = null;
            string? summary = null;
            string? result = null;
            string? preReqId = null;
            string? reward = null;

            foreach (var entry in metaFile)
            {
                var key = entry.Key;

                if (key.EndsWith("_start", StringComparison.Ordinal))
                {
                    //extract page from key prefix (e.g. "01_start" → page 1)
                    var underscoreIndex = key.IndexOf('_');

                    if ((underscoreIndex > 0)
                        && int.TryParse(
                            key[..underscoreIndex]
                                .Trim(),
                            out var page))
                        currentPage = page;

                    currentTitle = null;
                    currentId = null;
                    qualCircles = null;
                    qualClasses = null;
                    summary = null;
                    result = null;
                    preReqId = null;
                    reward = null;
                } else if (key.EndsWith("_title", StringComparison.Ordinal))
                    currentTitle = entry.Properties.Count > 0 ? entry.Properties[0] : string.Empty;
                else if (key.EndsWith("_id", StringComparison.Ordinal))
                    currentId = entry.Properties.Count > 0 ? entry.Properties[0] : string.Empty;
                else if (key.EndsWith("_qual", StringComparison.Ordinal))
                {
                    qualCircles = entry.Properties.Count > 0 ? entry.Properties[0] : string.Empty;
                    qualClasses = entry.Properties.Count > 1 ? entry.Properties[1] : string.Empty;
                } else if (key.EndsWith("_sum", StringComparison.Ordinal))
                    summary = entry.Properties.Count > 0 ? entry.Properties[0] : string.Empty;
                else if (key.EndsWith("_result", StringComparison.Ordinal))
                    result = entry.Properties.Count > 0 ? entry.Properties[0] : string.Empty;
                else if (key.EndsWith("_sub", StringComparison.Ordinal))
                    preReqId = entry.Properties.Count > 0 ? entry.Properties[0] : string.Empty;
                else if (key.EndsWith("_reward", StringComparison.Ordinal))
                    reward = entry.Properties.Count > 0 ? entry.Properties[0] : string.Empty;
                else if (key.EndsWith("_end", StringComparison.Ordinal) && currentTitle is not null)
                    events.Add(
                        new EventMetadataEntry
                        {
                            Title = currentTitle,
                            Id = currentId ?? string.Empty,
                            Page = currentPage,
                            QualifyingCircles = qualCircles ?? string.Empty,
                            QualifyingClasses = qualClasses ?? string.Empty,
                            Summary = summary ?? string.Empty,
                            Result = result ?? string.Empty,
                            PreRequisiteId = preReqId ?? string.Empty,
                            Reward = reward ?? string.Empty
                        });
            }
        }

        return events;
    }
}