#region
using DALib.Data;
#endregion

namespace Brigid.Data.Models;

/// <summary>
///     Parsed ability metadata for a single class. Contains separate skill and spell lists extracted from an SClass
///     metadata file.
/// </summary>
public sealed class AbilityMetadata
{
    //skills and spells are indexed separately: a class can legitimately have a skill and a spell of the same name.
    private readonly Dictionary<string, AbilityMetadataEntry> SkillsByName;
    private readonly Dictionary<string, AbilityMetadataEntry> SpellsByName;

    public IReadOnlyList<AbilityMetadataEntry> Skills { get; }
    public IReadOnlyList<AbilityMetadataEntry> Spells { get; }

    private AbilityMetadata(IReadOnlyList<AbilityMetadataEntry> skills, IReadOnlyList<AbilityMetadataEntry> spells)
    {
        Skills = skills;
        Spells = spells;
        SkillsByName = BuildIndex(skills);
        SpellsByName = BuildIndex(spells);
    }

    private static Dictionary<string, AbilityMetadataEntry> BuildIndex(IReadOnlyList<AbilityMetadataEntry> entries)
    {
        var index = new Dictionary<string, AbilityMetadataEntry>(entries.Count, StringComparer.OrdinalIgnoreCase);

        foreach (var entry in entries)
            index[entry.Name] = entry;

        return index;
    }

    /// <summary>
    ///     Looks an ability up by its display name (case-insensitive). False when this class's meta file has no such
    ///     entry — e.g. a server-only castable the client's SClass table doesn't know about.
    /// </summary>
    public bool TryGet(string? name, bool isSpell, out AbilityMetadataEntry? entry)
    {
        entry = null;

        return !string.IsNullOrEmpty(name) && (isSpell ? SpellsByName : SkillsByName).TryGetValue(name, out entry);
    }

    /// <summary>
    ///     Parses an SClass MetaFile into separate skill and spell lists.
    /// </summary>
    public static AbilityMetadata Parse(MetaFile metaFile)
    {
        var skills = new List<AbilityMetadataEntry>();
        var spells = new List<AbilityMetadataEntry>();
        var isSpellSection = false;
        var inSection = false;

        foreach (var entry in metaFile)
        {
            switch (entry.Key)
            {
                case "Skill":
                    isSpellSection = false;
                    inSection = true;

                    continue;
                case "Spell":
                    isSpellSection = true;
                    inSection = true;

                    continue;
                case "Skill_End":
                case "Spell_End":
                    inSection = false;

                    continue;
            }

            if (!inSection || (entry.Properties.Count < 2))
                continue;

            var parsed = ParseEntry(entry, isSpellSection);

            if (isSpellSection)
                spells.Add(parsed);
            else
                skills.Add(parsed);
        }

        return new AbilityMetadata(skills, spells);
    }

    /// <summary>
    ///     Parses a single MetaFileEntry into an AbilityMetadataEntry.
    /// </summary>
    /// <remarks>
    ///     Property layout: [0]=Level/IsMaster/AbilityLevel, [1]=IconId/0/0, [2]=Str/Int/Wis/Dex/Con,
    ///     [3]=PreReq1Name/Level, [4]=PreReq2Name/Level, [5]=Description.
    /// </remarks>
    private static AbilityMetadataEntry ParseEntry(MetaFileEntry entry, bool isSpell)
    {
        var props = entry.Properties;

        //[0] "{level}/{ismaster:0|1}/{abilitylevel}"
        var levelParts = props[0]
            .Split('/');

        int.TryParse(levelParts.ElementAtOrDefault(0), out var level);
        var requiresMaster = levelParts.ElementAtOrDefault(1) == "1";
        int.TryParse(levelParts.ElementAtOrDefault(2), out var abilityLevel);

        //[1] "{iconid}/0/0"
        var iconParts = props[1]
            .Split('/');

        ushort.TryParse(iconParts.ElementAtOrDefault(0), out var iconSprite);

        //[2] "{str}/{int}/{wis}/{dex}/{con}"
        byte str = 0,
             intStat = 0,
             wis = 0,
             dex = 0,
             con = 0;

        if (props.Count > 2)
        {
            var statParts = props[2]
                .Split('/');

            byte.TryParse(statParts.ElementAtOrDefault(0), out str);
            byte.TryParse(statParts.ElementAtOrDefault(1), out intStat);
            byte.TryParse(statParts.ElementAtOrDefault(2), out wis);
            byte.TryParse(statParts.ElementAtOrDefault(3), out dex);
            byte.TryParse(statParts.ElementAtOrDefault(4), out con);
        }

        //[3] "{prereq1name}/{prereq1level}"
        string? preReq1Name = null;
        byte preReq1Level = 0;

        if (props.Count > 3)
            ParsePreReq(props[3], out preReq1Name, out preReq1Level);

        //[4] "{prereq2name}/{prereq2level}"
        string? preReq2Name = null;
        byte preReq2Level = 0;

        if (props.Count > 4)
            ParsePreReq(props[4], out preReq2Name, out preReq2Level);

        //[5] "{description}"
        var description = props.Count > 5 ? props[5] : string.Empty;

        return new AbilityMetadataEntry
        {
            Name = entry.Key,
            IsSpell = isSpell,
            IconSprite = iconSprite,
            Level = level,
            RequiresMaster = requiresMaster,
            AbilityLevel = abilityLevel,
            Str = str,
            Int = intStat,
            Wis = wis,
            Dex = dex,
            Con = con,
            PreReq1Name = preReq1Name,
            PreReq1Level = preReq1Level,
            PreReq2Name = preReq2Name,
            PreReq2Level = preReq2Level,
            Description = description
        };
    }

    private static void ParsePreReq(string value, out string? name, out byte level)
    {
        var parts = value.Split('/');
        var rawName = parts.ElementAtOrDefault(0);
        name = rawName is null or "0" ? null : rawName;
        byte.TryParse(parts.ElementAtOrDefault(1), out level);
    }
}