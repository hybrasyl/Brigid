#region
using Chaos.DarkAges.Definitions;
#endregion

namespace Brigid.Data.Utilities;

public static class DataUtilities
{
    public static Gender DetermineGender(BodySprite bodySprite)
        => bodySprite switch
        {
            BodySprite.None        => 0,
            BodySprite.Male        => Gender.Male,
            BodySprite.Female      => Gender.Female,
            BodySprite.MaleGhost   => Gender.Male,
            BodySprite.FemaleGhost => Gender.Female,
            BodySprite.MaleInvis   => Gender.Male,
            BodySprite.FemaleInvis => Gender.Female,
            BodySprite.MaleJester  => Gender.Male,
            BodySprite.MaleHead    => Gender.Male,
            BodySprite.FemaleHead  => Gender.Female,
            BodySprite.BlankMale   => Gender.Male,
            BodySprite.BlankFemale => Gender.Female,
            _                      => 0
        };

    public static bool IsEmote(BodyAnimation bodyAnimation)
        => bodyAnimation switch
        {
            BodyAnimation.Smile       => true,
            BodyAnimation.Cry         => true,
            BodyAnimation.Frown       => true,
            BodyAnimation.Wink        => true,
            BodyAnimation.Surprise    => true,
            BodyAnimation.Tongue      => true,
            BodyAnimation.Pleasant    => true,
            BodyAnimation.Snore       => true,
            BodyAnimation.Mouth       => true,
            BodyAnimation.BlowKiss    => true,
            BodyAnimation.Wave        => true,
            BodyAnimation.RockOn      => true,
            BodyAnimation.Peace       => true,
            BodyAnimation.Stop        => true,
            BodyAnimation.Ouch        => true,
            BodyAnimation.Impatient   => true,
            BodyAnimation.Shock       => true,
            BodyAnimation.Pleasure    => true,
            BodyAnimation.Love        => true,
            BodyAnimation.SweatDrop   => true,
            BodyAnimation.Whistle     => true,
            BodyAnimation.Irritation  => true,
            BodyAnimation.Silly       => true,
            BodyAnimation.Cute        => true,
            BodyAnimation.Yelling     => true,
            BodyAnimation.Mischievous => true,
            BodyAnimation.Evil        => true,
            BodyAnimation.Horror      => true,
            BodyAnimation.PuppyDog    => true,
            BodyAnimation.StoneFaced  => true,
            BodyAnimation.Tears       => true,
            BodyAnimation.FiredUp     => true,
            BodyAnimation.Confused    => true,
            _                         => false
        };
}