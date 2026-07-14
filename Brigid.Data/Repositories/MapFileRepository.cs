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

        //map cache was relocated out of the read-only game folder to %LOCALAPPDATA%; must match the write side
        //(WorldScreen.SaveMapFile -> AppPaths.MapsDir), not the old {DataPath}/maps location.
        var path = Path.Combine(AppPaths.MapsDir, key);

        if (!File.Exists(path))
            return null;

        return MapFile.FromFile(path, width, height);
    }
}