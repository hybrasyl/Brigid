#region
using Chaos.Extensions.Common;
using DALib.Data;
using DALib.Extensions;
#endregion

namespace Brigid.Data;

public static class DatArchives
{
    public static DataArchive Cious { get; }
    public static DataArchive Hades { get; }
    public static DataArchive Ia { get; }
    public static DataArchive Khanmad { get; }
    public static DataArchive Khanmeh { get; }
    public static DataArchive Khanmim { get; }
    public static DataArchive Khanmns { get; }
    public static DataArchive Khanmtz { get; }
    public static DataArchive Khanpal { get; }
    public static DataArchive Khanwad { get; }
    public static DataArchive Khanweh { get; }
    public static DataArchive Khanwim { get; }
    public static DataArchive Khanwns { get; }
    public static DataArchive Khanwtz { get; }
    public static DataArchive Legend { get; }
    public static DataArchive Misc { get; }
    public static DataArchive National { get; }
    public static DataArchive Npcbase { get; }
    public static DataArchive Roh { get; }
    public static DataArchive Seo { get; }
    public static DataArchive Setoa { get; }

    static DatArchives()
    {
        Cious = Load(nameof(Cious));
        Hades = Load(nameof(Hades));
        Ia = Load(nameof(Ia));
        Khanmad = Load(nameof(Khanmad));
        Khanmeh = Load(nameof(Khanmeh));
        Khanmim = Load(nameof(Khanmim));
        Khanmns = Load(nameof(Khanmns));
        Khanmtz = Load(nameof(Khanmtz));
        Khanpal = Load(nameof(Khanpal));
        Khanwad = Load(nameof(Khanwad));
        Khanweh = Load(nameof(Khanweh));
        Khanwim = Load(nameof(Khanwim));
        Khanwns = Load(nameof(Khanwns));
        Khanwtz = Load(nameof(Khanwtz));
        Legend = Load(nameof(Legend));
        Misc = Load(nameof(Misc));
        National = Load(nameof(National));
        Roh = Load(nameof(Roh));
        Seo = Load(nameof(Seo));
        Setoa = Load(nameof(Setoa));
        Npcbase = Load(nameof(Npcbase));
    }

    public static DataArchive Load(string key)
    {
        key = key.WithExtension(".dat");

        if (key.StartsWithI("npcbase"))
            return DataArchive.FromFile(Path.Combine(DataContext.DataPath, "npc", key));

        var archive = DataArchive.FromFile(Path.Combine(DataContext.DataPath, key));

        return archive;
    }
}