#region
using System.Text;
using Chaos.Client.Data.Abstractions;
using Chaos.Client.Data.Models;
using DALib.Drawing;
using DALib.Drawing.Virtualized;
using DALib.Utility;
#endregion

namespace Chaos.Client.Data.Repositories;

public sealed class EffectsRepository : RepositoryBase
{
    private readonly PaletteLookup EffectPalettes = PaletteLookup.FromArchive("effpal", "eff", DatArchives.Roh)
                                                                 .Freeze();

    private readonly EffectTable EffectTable;
    private readonly ProjectileInfo?[] ProjectileDetails;

    public EffectsRepository()
    {
        if (DatArchives.Roh.TryGetValue("effect.tbl", out _))
            EffectTable = EffectTable.FromArchive(DatArchives.Roh);
        else
            EffectTable = new EffectTable();

        ProjectileDetails = ParseMeffectTable();
    }

    /// <summary>
    ///     Loads a frame-based animation effect (EFA format) from the Roh archive.
    /// </summary>
    public EfaView? GetEfaEffect(int effectId)
    {
        if (effectId == 0)
            return null;

        var entryName = $"efct{effectId:D3}.efa";

        if (!DatArchives.Roh.TryGetValue(entryName, out var entry))
            return null;

        return GetOrCreate($"efa_{effectId}", () => EfaView.FromEntry(entry));
    }

    /// <summary>
    ///     Loads per-frame center points from an effect's .tbl file (e.g. eff246.tbl). Returns null if no .tbl file exists for
    ///     this effect.
    /// </summary>
    public (short X, short Y)[]? GetEffectCenterPoints(int effectId, int frameCount)
    {
        if (effectId <= 0)
            return null;

        var tblName = $"efct{effectId:D3}.tbl";

        if (!DatArchives.Roh.TryGetValue(tblName, out var tblEntry))
            return null;

        using var stream = tblEntry.ToStreamSegment();
        using var reader = new BinaryReader(stream);

        var points = new (short X, short Y)[frameCount];

        for (var i = 0; (i < frameCount) && (stream.Position < stream.Length); i++)
        {
            var x = reader.ReadInt16();
            var y = reader.ReadInt16();
            points[i] = (x, y);
        }

        //missing entries fall back to the standard tile-bottom-center anchor
        for (var i = 0; i < frameCount; i++)
            if (points[i] == default)
                points[i] = (28, 70);

        return points;
    }

    /// <summary>
    ///     Returns the EffectTable entry for an effect, or null if the effect has no table entry.
    /// </summary>
    /// <remarks>
    ///     A single-element entry containing [0] indicates the effect uses EFA format rather than EPF frame sequences.
    /// </remarks>
    public EffectTableEntry? GetEffectTableEntry(int effectId)
    {
        if (effectId <= 0)
            return null;

        return EffectTable.TryGetEntry(effectId, out var entry) ? entry : null;
    }

    /// <summary>
    ///     Loads a palettized sprite-based effect (EPF format) from the Roh archive.
    /// </summary>
    public Palettized<EpfView>? GetEpfEffect(int effectId)
    {
        if (effectId == 0)
            return null;

        var entryName = $"efct{effectId:D3}.epf";

        if (!DatArchives.Roh.TryGetValue(entryName, out var entry))
            return null;

        return GetOrCreate(
            $"epf_{effectId}",
            () => new Palettized<EpfView>
            {
                Entity = EpfView.FromEntry(entry),
                Palette = EffectPalettes.GetPaletteForId(effectId)
            });
    }

    /// <summary>
    ///     Returns true if the effect uses luminance-based alpha blending instead of standard palette rendering.
    /// </summary>
    public bool UsesLuminanceBlending(int effectId) => EffectPalettes.Table.GetPaletteNumber(effectId) >= 1000;

    public ProjectileInfo? GetMeffectRecord(int meffectId)
    {
        if ((meffectId < 0) || (meffectId >= ProjectileDetails.Length))
            return null;

        return ProjectileDetails[meffectId];
    }

    public SpfFile? GetMefcSprite(int mefcId)
    {
        var entryName = $"mefc{mefcId:D3}.spf";

        if (!DatArchives.Roh.TryGetValue(entryName, out var entry))
            return null;

        return GetOrCreate($"mefc_spf_{mefcId}", () => SpfFile.FromEntry(entry));
    }

    private static ProjectileInfo?[] ParseMeffectTable()
    {
        if (!DatArchives.Roh.TryGetValue("meffect.tbl", out var entry))
            return [];

        var text = Encoding.ASCII.GetString(entry.ToSpan());

        var lines = text.Split(
            [
                '\r',
                '\n'
            ],
            StringSplitOptions.RemoveEmptyEntries);

        var records = new List<ProjectileInfo>();
        var maxId = -1;
        var countLineSeen = false;

        foreach (var line in lines)
        {
            var trimmed = line.Trim();

            if (trimmed.StartsWith(';') || trimmed.StartsWith('#') || (trimmed.Length == 0))
                continue;

            //first non-comment line is the entry count -- skip it
            if (!countLineSeen)
            {
                countLineSeen = true;

                continue;
            }

            var tokens = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            //minimum: id type frames "distance" step step_delay
            if (tokens.Length < 6)
                continue;

            //only "distance" mode is supported
            if (!tokens[3].Equals("distance", StringComparison.OrdinalIgnoreCase))
                continue;

            if (!int.TryParse(tokens[0], out var id)
                || !int.TryParse(tokens[1], out var type)
                || !int.TryParse(tokens[2], out var frames)
                || !int.TryParse(tokens[4], out var step)
                || !int.TryParse(tokens[5], out var stepDelay))
                continue;

            int? arcV = null;
            int? arcH = null;

            if (tokens.Length > 6)
            {
                var arcParts = tokens[6].Split('/');

                if ((arcParts.Length == 2)
                    && int.TryParse(arcParts[0], out var v)
                    && int.TryParse(arcParts[1], out var h))
                {
                    arcV = v;
                    arcH = h;
                }
            }

            var record = new ProjectileInfo
            {
                Id = id,
                Type = type,
                FramesPerDirection = frames,
                Step = step,
                StepDelay = stepDelay,
                ArcRatioV = arcV,
                ArcRatioH = arcH
            };

            records.Add(record);

            if (id > maxId)
                maxId = id;
        }

        if (maxId < 0)
            return [];

        var result = new ProjectileInfo?[maxId + 1];

        foreach (var record in records)
            result[record.Id] = record;

        return result;
    }
}