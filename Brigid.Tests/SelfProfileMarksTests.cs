#region
using Brigid.Models;
using Brigid.ViewModel;
using Microsoft.Xna.Framework;
using Xunit;
#endregion

namespace Brigid.Tests;

// The Legend window and the activity log read the marks of the profile, which raised one Changed for its name, portrait
// and text too; each kept the list that it read and compared the reference. The profile raises LegendMarksChanged now,
// only when the marks change or the profile comes or goes.
public sealed class SelfProfileMarksTests
{
    private static LegendMarkEntry Mark(string text) => new(text, Color.White, 0, string.Empty);

    [Fact]
    public void TheMarksRaiseTheirEventOnlyWhenTheyChange()
    {
        var profile = new SelfProfile();
        var raised = 0;
        profile.LegendMarksChanged += () => raised++;

        //the first profile raises it, also with no marks: the activity log reads its start
        profile.Update("Wizard", "", "", "", "", []);
        Assert.Equal(1, raised);

        //a new list with the same marks, the name and the text do not
        profile.Update("Wizard", "Guild", "", "", "", []);
        profile.SetName("Angelique");
        profile.SetProfileText("Hi.");
        Assert.Equal(1, raised);

        profile.Update("Wizard", "", "", "", "", [Mark("Born")]);
        Assert.Equal(2, raised);

        profile.Update("Wizard", "", "", "", "", [Mark("Born")]);
        Assert.Equal(2, raised);

        //a logout forgets the marks and the profile
        profile.Clear();
        Assert.Equal(3, raised);

        //a second clear changes nothing
        profile.Clear();
        Assert.Equal(3, raised);
    }
}
