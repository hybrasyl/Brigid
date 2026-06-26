#region
using System.Buffers.Binary;
using System.IO.Compression;
using System.Net;
using System.Text;
#endregion

namespace Brigid.Networking;

/// <summary>
///     Represents a single server entry from the server table.
/// </summary>
public sealed record ServerTableEntry(
    byte Id,
    string Address,
    int Port,
    string Name,
    string Description);

/// <summary>
///     Represents the parsed server table received from the lobby server.
/// </summary>
public sealed class ServerTableData
{
    /// <summary>
    ///     The list of available servers.
    /// </summary>
    public required List<ServerTableEntry> Servers { get; init; }

    /// <summary>
    ///     Whether the client should display the server selection list to the user.
    /// </summary>
    public required bool ShowServerList { get; init; }

    private static byte[] Decompress(byte[] compressedData)
    {
        // Hybrasyl writes a zlib wrapper (0x78 0x9C header + deflate + Adler32) but produces
        // an Adler32 that .NET's strict ZLibStream rejects. Strip the 2-byte header and 4-byte
        // trailer and decompress the raw deflate body directly.
        var deflateLength = compressedData.Length - 6;
        using var compressedStream = new MemoryStream(compressedData, 2, deflateLength);
        using var deflateStream = new DeflateStream(compressedStream, CompressionMode.Decompress);
        using var decompressedStream = new MemoryStream();

        deflateStream.CopyTo(decompressedStream);

        return decompressedStream.ToArray();
    }

    /// <summary>
    ///     Parses a server table from Zlib-compressed binary data.
    /// </summary>
    /// <param name="compressedData">
    ///     The raw Zlib-compressed server table bytes from the lobby server.
    /// </param>
    public static ServerTableData Parse(byte[] compressedData)
    {
        var data = Decompress(compressedData);

        if (data.Length == 0)
            return new ServerTableData
            {
                Servers = [],
                ShowServerList = false
            };

        var offset = 0;
        var serverCount = data[offset++];
        var koreanEncoding = Encoding.GetEncoding(949);
        var servers = new List<ServerTableEntry>(serverCount);

        for (var i = 0; i < serverCount; i++)
        {
            if ((offset + 7) > data.Length)
                break;

            var serverId = data[offset++];

            //ip address: 4 raw bytes in network byte order
            var ipAddress = new IPAddress(data.AsSpan(offset, 4));
            offset += 4;

            //port: big-endian uint16
            var port = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(offset, 2));
            offset += 2;

            //null-terminated string in codepage 949: "{name};{description}\0"
            var stringStart = offset;

            while ((offset < data.Length) && (data[offset] != 0))
                offset++;

            var rawString = koreanEncoding.GetString(data, stringStart, offset - stringStart);

            //skip past the null terminator
            if (offset < data.Length)
                offset++;

            //split on ';' to get name and description
            var separatorIndex = rawString.IndexOf(';');
            string name;
            string description;

            if (separatorIndex >= 0)
            {
                name = rawString[..separatorIndex];
                description = rawString[(separatorIndex + 1)..];
            } else
            {
                name = rawString;
                description = string.Empty;
            }

            servers.Add(
                new ServerTableEntry(
                    serverId,
                    ipAddress.ToString(),
                    port,
                    name,
                    description));
        }

        //the trailing byte after all server entries is the showserverlist flag
        var showServerList = (offset < data.Length) && (data[offset] != 0);

        return new ServerTableData
        {
            Servers = servers,
            ShowServerList = showServerList
        };
    }
}