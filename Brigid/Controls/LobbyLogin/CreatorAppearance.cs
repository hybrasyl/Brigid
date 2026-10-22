#region
using Chaos.DarkAges.Definitions;
#endregion

namespace Brigid.Controls.LobbyLogin;

/// <summary>
///     The appearance a player picks in the character creator (BRIG-116): gender, hair style, hair colour and the facing of
///     the preview. It holds the rules of the legacy creator, so that a form only shows and changes it. The facing is for
///     the preview only; the create packets do not carry it.
/// </summary>
public sealed class CreatorAppearance
{
    public const int MAX_MALE_HAIR_STYLE = 18;
    public const int MAX_FEMALE_HAIR_STYLE = 17;

    private const int DIRECTION_COUNT = 4;
    private const int DEFAULT_DIRECTION = 1;

    /// <summary>The hair colours the creator offers, in the order of the legacy 7 × 2 swatch.</summary>
    public static IReadOnlyList<DisplayColor> HairColors { get; } =
    [
        DisplayColor.Teal,
        DisplayColor.Green,
        DisplayColor.Olive,
        DisplayColor.Yellow,
        DisplayColor.Pumpkin,
        DisplayColor.Apple,
        DisplayColor.Violet,
        DisplayColor.Default,
        DisplayColor.Navy,
        DisplayColor.Blue,
        DisplayColor.Gray,
        DisplayColor.Carrot,
        DisplayColor.Brown,
        DisplayColor.Black
    ];

    public Gender Gender { get; private set; } = Gender.Male;
    public byte HairStyle { get; private set; } = 1;
    public DisplayColor HairColor { get; private set; } = DisplayColor.Default;

    /// <summary>The preview direction, 0 to 3 (up, right, down, left).</summary>
    public int Direction { get; private set; } = DEFAULT_DIRECTION;

    /// <summary>The highest hair style for the selected gender. The lowest is 1.</summary>
    public int MaxHairStyle => Gender == Gender.Male ? MAX_MALE_HAIR_STYLE : MAX_FEMALE_HAIR_STYLE;

    /// <summary>Raised after each change to the appearance.</summary>
    public event Action? Changed;

    /// <summary>Selects a gender. A hair style above the new limit moves down to the limit.</summary>
    public void SetGender(Gender gender)
    {
        if (Gender == gender)
            return;

        Gender = gender;

        if (HairStyle > MaxHairStyle)
            HairStyle = (byte)MaxHairStyle;

        Changed?.Invoke();
    }

    /// <summary>Selects the next hair style. After the last style, the first style is selected.</summary>
    public void NextHairStyle()
    {
        HairStyle = HairStyle >= MaxHairStyle ? (byte)1 : (byte)(HairStyle + 1);
        Changed?.Invoke();
    }

    /// <summary>Selects the previous hair style. Before the first style, the last style is selected.</summary>
    public void PreviousHairStyle()
    {
        HairStyle = HairStyle <= 1 ? (byte)MaxHairStyle : (byte)(HairStyle - 1);
        Changed?.Invoke();
    }

    /// <summary>Selects a hair colour. A colour that the creator does not offer is ignored.</summary>
    public void SetHairColor(DisplayColor color)
    {
        if ((HairColor == color) || !HairColors.Contains(color))
            return;

        HairColor = color;
        Changed?.Invoke();
    }

    /// <summary>Turns the preview one direction clockwise. The legacy creator's left arrow did this.</summary>
    public void TurnClockwise()
    {
        Direction = (Direction + 1) % DIRECTION_COUNT;
        Changed?.Invoke();
    }

    /// <summary>Turns the preview one direction counter-clockwise. The legacy creator's right arrow did this.</summary>
    public void TurnCounterClockwise()
    {
        Direction = (Direction + DIRECTION_COUNT - 1) % DIRECTION_COUNT;
        Changed?.Invoke();
    }

    /// <summary>Sets the appearance that a new creation starts with.</summary>
    public void Reset()
    {
        Gender = Gender.Male;
        HairStyle = 1;
        HairColor = DisplayColor.Default;
        Direction = DEFAULT_DIRECTION;
        Changed?.Invoke();
    }
}
