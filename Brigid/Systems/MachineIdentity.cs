#region
using System.Runtime.Versioning;
using System.Xml.Linq;
using Microsoft.Win32;
#endregion

namespace Brigid.Systems;

public static class MachineIdentity
{
    private const uint DEFAULT_CLIENT_ID1 = 4278255360;
    private const uint DEFAULT_CLIENT_ID2 = 4278255360;

    public static uint ClientId1 { get; }
    public static uint ClientId2 { get; }

    static MachineIdentity()
    {
        try
        {
            if (OperatingSystem.IsWindows())
                (ClientId1, ClientId2) = LoadOrCreateWindows();
            else if (OperatingSystem.IsMacOS())
                (ClientId1, ClientId2) = LoadOrCreateMacOs();
            else if (OperatingSystem.IsLinux())
                (ClientId1, ClientId2) = LoadOrCreateLinux();
            else
                (ClientId1, ClientId2) = (DEFAULT_CLIENT_ID1, DEFAULT_CLIENT_ID2);
        } catch
        {
            ClientId1 = DEFAULT_CLIENT_ID1;
            ClientId2 = DEFAULT_CLIENT_ID2;
        }
    }

    private static (uint Id1, uint Id2) Generate()
    {
        var id1 = (uint)Random.Shared.Next(1, int.MaxValue);
        var id2 = (uint)Random.Shared.Next(1, int.MaxValue);

        return (id1, id2);
    }

    #region Windows - Registry keys disguised as COM control registrations
    [SupportedOSPlatform("windows")]
    private static (uint, uint) LoadOrCreateWindows()
    {
        //read from hkcr (merged view of hklm + hkcu, no admin needed for reads)
        using var readKey1 = Registry.ClassesRoot.OpenSubKey(@"NXKRI.Ctrl.1");
        using var readKey2 = Registry.ClassesRoot.OpenSubKey(@"KRIHC.Ctrl.1");

        var val1 = readKey1?.GetValue("CLSID");
        var val2 = readKey2?.GetValue("CLSID");

        if (val1 is int i1 && val2 is int i2)
            return (unchecked((uint)i1), unchecked((uint)i2));

        (var newId1, var newId2) = Generate();

        //write to hkcu\software\classes (no admin needed, visible via hkcr)
        using var writeKey1 = Registry.CurrentUser.CreateSubKey(@"Software\Classes\NXKRI.Ctrl.1");
        using var writeKey2 = Registry.CurrentUser.CreateSubKey(@"Software\Classes\KRIHC.Ctrl.1");

        writeKey1.SetValue("CLSID", unchecked((int)newId1), RegistryValueKind.DWord);
        writeKey2.SetValue("CLSID", unchecked((int)newId2), RegistryValueKind.DWord);

        return (newId1, newId2);
    }
    #endregion

    #region macOS - Plist files disguised as app preferences
    [SupportedOSPlatform("macos")]
    private static (uint, uint) LoadOrCreateMacOs()
    {
        var prefsDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Preferences");

        var plist1 = Path.Combine(prefsDir, "com.nxkri.Ctrl.1.plist");
        var plist2 = Path.Combine(prefsDir, "com.krihc.Ctrl.1.plist");

        var id1 = ReadPlistDword(plist1);
        var id2 = ReadPlistDword(plist2);

        if (id1.HasValue && id2.HasValue)
            return (id1.Value, id2.Value);

        (var newId1, var newId2) = Generate();

        WritePlistDword(plist1, newId1);
        WritePlistDword(plist2, newId2);

        return (newId1, newId2);
    }

    private static uint? ReadPlistDword(string path)
    {
        if (!File.Exists(path))
            return null;

        var doc = XDocument.Load(path);

        var elements = doc.Root
                          ?.Element("dict")
                          ?.Elements()
                          .ToList();

        if (elements is null)
            return null;

        for (var i = 0; i < (elements.Count - 1); i++)
            if ((elements[i].Name == "key")
                && (elements[i].Value == "CLSID")
                && (elements[i + 1].Name == "integer")
                && uint.TryParse(elements[i + 1].Value, out var val))
                return val;

        return null;
    }

    private static void WritePlistDword(string path, uint value)
        => File.WriteAllText(
            path,
            $"""
             <?xml version="1.0" encoding="UTF-8"?>
             <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
             <plist version="1.0">
             <dict>
                 <key>CLSID</key>
                 <integer>{value}</integer>
             </dict>
             </plist>
             """);
    #endregion

    #region Linux - Binary files disguised as app data
    [SupportedOSPlatform("linux")]
    private static (uint, uint) LoadOrCreateLinux()
    {
        var dataDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        if (string.IsNullOrEmpty(dataDir))
            dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");

        var file1 = Path.Combine(dataDir, "nxkri.ctrl.1");
        var file2 = Path.Combine(dataDir, "krihc.ctrl.1");

        var id1 = ReadBinaryDword(file1);
        var id2 = ReadBinaryDword(file2);

        if (id1.HasValue && id2.HasValue)
            return (id1.Value, id2.Value);

        (var newId1, var newId2) = Generate();

        WriteBinaryDword(file1, newId1);
        WriteBinaryDword(file2, newId2);

        return (newId1, newId2);
    }

    private static uint? ReadBinaryDword(string path)
    {
        if (!File.Exists(path))
            return null;

        var bytes = File.ReadAllBytes(path);

        if (bytes.Length < 4)
            return null;

        return BitConverter.ToUInt32(bytes, 0);
    }

    private static void WriteBinaryDword(string path, uint value)
    {
        var dir = Path.GetDirectoryName(path);

        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        File.WriteAllBytes(path, BitConverter.GetBytes(value));
    }
    #endregion
}