using DALib.Networking.Packets.Server;

namespace Brigid.Tests;

// Builds 0x33 display packets for WorldState tests. Every field the packet requires gets a neutral value.
internal static class DisplayUserPackets
{
    public static DisplayUserPacket Display(uint id, DisplayUserAppearance appearance)
        => new()
        {
            Id = id,
            Appearance = appearance,
            X = 1,
            Y = 1,
            Direction = default,
            Name = "Test",
            NameTagStyle = default
        };

    //the body byte is gender * 16 + form offset, as Hybrasyl's User.SendUpdateToUser writes it: 0x10 male, 0x30 a dead
    //male (offset 0x20), 0x40 a dead female
    public static EquipmentAppearance Body(byte bodySprite = 0x10, bool isHidden = false, byte bodyColor = 0)
        => new()
        {
            BodySprite = bodySprite,
            IsHidden = isHidden,
            BodyColor = bodyColor,
            HeadSprite = default,
            HeadColor = default,
            FaceSprite = default,
            ArmorSprite1 = default,
            ArmorSprite2 = default,
            BootsSprite = default,
            BootsColor = default,
            ShieldSprite = default,
            WeaponSprite = default,
            OvercoatSprite = default,
            OvercoatColor = default,
            AccessorySprite1 = default,
            AccessorySprite2 = default,
            AccessorySprite3 = default,
            AccessoryColor1 = default,
            AccessoryColor2 = default,
            AccessoryColor3 = default,
            LanternSize = default,
            RestPosition = default
        };

    //the wire carries a creature sprite with the 0x4000 creature-range offset
    public static CreatureSpriteAppearance Monster(int sprite)
        => new() { Sprite = (ushort)(0x4000 + sprite), HeadColor = default, BootsColor = default };
}
