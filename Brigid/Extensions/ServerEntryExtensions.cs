#region
using DALib.Networking.Packets.Server;
#endregion

namespace Brigid.Extensions;

public static class ServerEntryExtensions
{
    extension(ServerEntry entry)
    {
        /// <summary>
        ///     The 0x56 server-entry cstring carries <c>name;description</c> in one field; DALib keeps it unsplit.
        /// </summary>
        public (string Name, string Description) SplitNameDescription()
        {
            var separator = entry.Name.IndexOf(';');

            return separator < 0
                ? (entry.Name, string.Empty)
                : (entry.Name[..separator], entry.Name[(separator + 1)..]);
        }
    }
}
