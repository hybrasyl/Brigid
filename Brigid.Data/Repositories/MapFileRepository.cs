#region
using DALib.Data;
using DALib.Extensions;
#endregion

namespace Brigid.Data.Repositories;

public sealed class MapFileRepository
{
    public MapFile? GetMapFile(string key, int width, int height)
    {
        key = key.WithExtension(".map");
        var path = Path.Combine(DataContext.DataPath, "maps", key);

        if (!File.Exists(path))
            return null;

        return MapFile.FromFile(path, width, height);
    }
}