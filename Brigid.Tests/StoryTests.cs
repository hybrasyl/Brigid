#region
using Brigid.Controls.Components;
using Brigid.Controls.Generic;
using Brigid.Controls.LobbyLogin;
using Brigid.Definitions;
using Brigid.Rendering;
using Microsoft.Xna.Framework.Input;
using Xunit;
#endregion

namespace Brigid.Tests;

/// <summary>
///     The BRIG-107 Story: <c>story.json</c> parsing keeps the good pages and reports the bad ones, the shipped story is
///     valid, and the view moves through the pages and closes after the last one.
/// </summary>
[Collection("FontEngine")]
public class StoryTests
{
    public StoryTests() => FontEngine.Initialize(0);

    [Fact]
    public void ThePagesAreReadInTheirOrder()
    {
        var script = StoryScript.Parse(
            """
            { "pages": [
                { "kind": "slide", "image": "story.01", "text": "one" },
                { "kind": "Text", "title": "Two", "text": "**two**" },
                { "kind": "cutscene", "video": "intro", "text": "three" }
            ] }
            """);

        Assert.Equal(
            [
                new StoryPage(StoryPageKind.Slide, "one", Image: "story.01"),
                new StoryPage(StoryPageKind.Text, "**two**", "Two"),
                new StoryPage(StoryPageKind.Cutscene, "three", Video: "intro")
            ],
            script.Pages);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{ }")]
    [InlineData("{ \"pages\": {} }")]
    public void TextThatIsNotAStoryGivesNoPages(string json)
    {
        var warnings = new List<string>();

        var script = StoryScript.Parse(json, warnings.Add);

        Assert.Empty(script.Pages);
        Assert.Single(warnings);
    }

    [Theory]
    [InlineData("7")]
    [InlineData("{ \"text\": \"no kind\" }")]
    [InlineData("{ \"kind\": \"video\" }")]
    [InlineData("{ \"kind\": \"1\" }")]
    [InlineData("{ \"kind\": 1 }")]
    [InlineData("{ \"kind\": \"slide, text\" }")]
    public void ABadPageIsSkippedAndTheRestStay(string badPage)
    {
        var warnings = new List<string>();

        var script = StoryScript.Parse($$"""{ "pages": [ {{badPage}}, { "kind": "text", "text": "kept" } ] }""", warnings.Add);

        Assert.Equal("kept", Assert.Single(script.Pages).Text);
        Assert.NotEmpty(warnings);
    }

    [Fact]
    public void ATrailingCommaAndACommentAreAccepted()
    {
        var warnings = new List<string>();

        var script = StoryScript.Parse(
            """
            { "pages": [
                // the first page
                { "kind": "text", "text": "one", },
            ] }
            """,
            warnings.Add);

        Assert.Single(script.Pages);
        Assert.Empty(warnings);
    }

    [Fact]
    public void AltEnterDoesNotTurnThePage()
    {
        var view = new StoryView();
        view.Show(ThreePages());

        view.OnKeyDown(new KeyDownEvent { Key = Keys.Enter, Modifiers = KeyModifiers.Alt });

        Assert.Equal(0, view.PageIndex);
    }

    [Fact]
    public void ASlideWithNoImageStaysForThePlaceholder()
    {
        var warnings = new List<string>();

        var script = StoryScript.Parse("""{ "pages": [ { "kind": "slide", "text": "caption" } ] }""", warnings.Add);

        Assert.Null(Assert.Single(script.Pages).Image);
        Assert.Single(warnings);
    }

    [Fact]
    public void AValueThatIsNotAStringIsReadAsAbsent()
    {
        var warnings = new List<string>();

        var script = StoryScript.Parse("""{ "pages": [ { "kind": "text", "text": 5, "title": null } ] }""", warnings.Add);

        var page = Assert.Single(script.Pages);
        Assert.Equal(string.Empty, page.Text);
        Assert.Null(page.Title);
        Assert.Single(warnings);
    }

    [Fact]
    public void TheShippedStoryIsValidAndHasEachKind()
    {
        var warnings = new List<string>();

        var script = StoryScript.Parse(MainMenuControl.ReadText("story.json"), warnings.Add);

        Assert.Empty(warnings);
        Assert.All(Enum.GetValues<StoryPageKind>(), kind => Assert.Contains(script.Pages, page => page.Kind == kind));
    }

    [Fact]
    public void NextAndBackMoveThroughThePages()
    {
        var view = new StoryView();
        view.Show(ThreePages());

        Assert.True(view.Visible);
        Assert.Equal(0, view.PageIndex);
        Assert.False(view.BackButton.Enabled);

        view.Next();
        view.Next();

        Assert.Equal(2, view.PageIndex);
        Assert.True(view.BackButton.Enabled);

        view.Back();

        Assert.Equal(1, view.PageIndex);
    }

    [Fact]
    public void BackOnTheFirstPageStays()
    {
        var view = new StoryView();
        view.Show(ThreePages());

        view.Back();

        Assert.Equal(0, view.PageIndex);
        Assert.True(view.Visible);
    }

    [Fact]
    public void NextOnTheLastPageCloses()
    {
        var view = new StoryView();
        var closed = 0;
        view.OnClose += () => closed++;
        view.Show(ThreePages());

        view.Next();
        view.Next();

        Assert.Equal("3 / 3", view.FindChild<UILabel>("counter")!.Text);

        view.Next();

        Assert.False(view.Visible);
        Assert.Equal(1, closed);
    }

    [Fact]
    public void AStoryWithNoPagesDoesNotOpen()
    {
        var view = new StoryView();

        view.Show(StoryScript.Empty);

        Assert.False(view.Visible);
    }

    [Fact]
    public void ShowStartsAgainFromTheFirstPage()
    {
        var view = new StoryView();
        view.Show(ThreePages());
        view.Next();
        view.Hide();

        view.Show(ThreePages());

        Assert.Equal(0, view.PageIndex);
    }

    [Theory]
    [InlineData(Keys.Right, 2)]
    [InlineData(Keys.Space, 2)]
    [InlineData(Keys.Enter, 2)]
    [InlineData(Keys.Left, 0)]
    public void TheKeysMoveThroughThePages(Keys key, int expected)
    {
        var view = new StoryView();
        view.Show(ThreePages());
        view.Next();

        var e = new KeyDownEvent { Key = key };
        view.OnKeyDown(e);

        Assert.Equal(expected, view.PageIndex);
        Assert.True(e.Handled);
    }

    [Fact]
    public void EscapeClosesAndOtherKeysStopAtTheView()
    {
        var view = new StoryView();
        view.Show(ThreePages());

        var other = new KeyDownEvent { Key = Keys.A };
        view.OnKeyDown(other);

        Assert.True(other.Handled);
        Assert.True(view.Visible);

        view.OnKeyDown(new KeyDownEvent { Key = Keys.Escape });

        Assert.False(view.Visible);
    }

    [Fact]
    public void ACaptionFitsItsText()
    {
        var view = new StoryView();
        view.Show(new StoryScript([new StoryPage(StoryPageKind.Slide, "short", Image: "story.01"), new StoryPage(StoryPageKind.Slide, string.Concat(Enumerable.Repeat("long text ", 120)), Image: "story.02")]));

        var frame = view.FindChild<FramePanel>("frame")!;
        var shortHeight = frame.Height;

        view.Next();

        Assert.True(frame.Height > shortHeight);

        //the caption box stays above the buttons
        Assert.True(frame.Y + frame.Height <= view.NextButton.ScreenY);
    }

    private static StoryScript ThreePages()
        => new(
        [
            new StoryPage(StoryPageKind.Slide, "one", Image: "story.01"),
            new StoryPage(StoryPageKind.Text, "two", "Two"),
            new StoryPage(StoryPageKind.Cutscene, "three", Video: "intro")
        ]);
}
