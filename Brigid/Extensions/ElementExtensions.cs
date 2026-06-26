using Chaos.DarkAges.Definitions;

namespace Brigid.Extensions;

public static class ElementExtensions
{
    extension(Element element)
    {
        public string Name
            => element switch
            {
                Element.Fire     => "Fire",
                Element.Water    => "Water",
                Element.Wind     => "Wind",
                Element.Earth    => "Earth",
                Element.Holy     => "Light",
                Element.Darkness => "Dark",
                Element.Wood     => "Wood",
                Element.Metal    => "Metal",
                Element.Undead   => "Undead",
                _                => "None"
            };
    }
}