using System.Diagnostics;
using Xunit;

namespace Brigid.SmokeTests;

// The harness rules that need no client, no data and no server, so they run in every test run. A second smoke run took
// down the stack and deleted the files of a run that still went, and a run with BRIGID_SMOKE=1 and bad paths skipped
// every test and exited 0.
public sealed class HarnessTests
{
    private static Func<string, string?> Environment(params (string Name, string Value)[] values)
        => name => values.FirstOrDefault(value => value.Name == name).Value;

    [Fact]
    public void ANewNameCarriesTheProcessIdOfThisTestHost()
    {
        var name = SmokeOwner.NewName(8);

        Assert.Equal(System.Environment.ProcessId, SmokeOwner.OwnerOf(name));
        Assert.Equal($"{SmokeOwner.PREFIX}{System.Environment.ProcessId}-".Length + 8, name.Length);
    }

    [Theory]
    [InlineData("brigid-smoke-1234-abcd1234", 1234)]
    [InlineData("brigid-smoke-1234-abcd1234.config.xml", 1234)]
    [InlineData("brigid-smoke-0123456789abcdef0123456789abcdef", null)]
    [InlineData("brigid-smoke--abcd", null)]
    [InlineData("other-1234-abcd", null)]
    public void TheOwnerIsTheProcessIdInTheName(string name, int? owner) => Assert.Equal(owner, SmokeOwner.OwnerOf(name));

    [Fact]
    public void ANameOfARunThatStillGoesIsNotStaleWhateverItsAge()
        => Assert.False(SmokeOwner.IsStale(SmokeOwner.NewName(8), DateTime.MinValue, TimeSpan.Zero));

    [Fact]
    public void ANameOfAStoppedRunIsStale()
    {
        using var stopped = Process.Start(new ProcessStartInfo("dotnet", "--version") { RedirectStandardOutput = true })!;
        stopped.WaitForExit();

        Assert.True(SmokeOwner.IsStale($"{SmokeOwner.PREFIX}{stopped.Id}-abcd1234", DateTime.UtcNow, TimeSpan.FromHours(1)));
    }

    //a name from before the process id was in it is stale by its age
    [Fact]
    public void ANameWithNoOwnerIsStaleByItsAge()
    {
        const string old = "brigid-smoke-0123456789abcdef0123456789abcdef";

        Assert.True(SmokeOwner.IsStale(old, DateTime.UtcNow.AddHours(-2), TimeSpan.FromHours(1)));
        Assert.False(SmokeOwner.IsStale(old, DateTime.UtcNow, TimeSpan.FromHours(1)));
    }

    [Fact]
    public void WithoutTheSwitchEveryTestSkips()
    {
        Assert.NotNull(SmokeEnvironment.SkipReasonFor(Environment()));
        Assert.NotNull(SmokeEnvironment.ServerSkipReasonFor(Environment(("BRIGID_SMOKE_SERVER", "server"))));
    }

    //with the switch on, a bad data folder is a failure, not a skip
    [Fact]
    public void WithTheSwitchABadDataFolderFailsAndDoesNotSkip()
    {
        var environment = Environment(("BRIGID_SMOKE", "1"), ("BRIGID_SMOKE_DATA", Path.Combine(Path.GetTempPath(), "brigid-no-such-folder")));

        Assert.Null(SmokeEnvironment.SkipReasonFor(environment));
        Assert.NotNull(SmokeEnvironment.DataErrorFor(environment));
        Assert.NotNull(SmokeEnvironment.DataErrorFor(Environment(("BRIGID_SMOKE", "1"))));
    }

    [Fact]
    public void ADataFolderWithTheArchivesHasNoError()
    {
        var data = Directory.CreateTempSubdirectory("brigid-smoke-data-").FullName;

        try
        {
            File.WriteAllBytes(Path.Combine(data, "legend.dat"), [0]);

            Assert.Null(SmokeEnvironment.DataErrorFor(Environment(("BRIGID_SMOKE_DATA", data))));
        } finally
        {
            Directory.Delete(data, true);
        }
    }

    //a server checkout that is not set skips the server tests; one that is set and wrong fails them
    [Fact]
    public void AServerCheckoutThatIsNotSetSkipsAndAWrongOneFails()
    {
        var unset = Environment(("BRIGID_SMOKE", "1"));
        var wrong = Environment(("BRIGID_SMOKE", "1"), ("BRIGID_SMOKE_SERVER", Path.GetTempPath()));

        Assert.NotNull(SmokeEnvironment.ServerSkipReasonFor(unset));
        Assert.Null(SmokeEnvironment.ServerErrorFor(unset));
        Assert.Null(SmokeEnvironment.ServerSkipReasonFor(wrong));
        Assert.NotNull(SmokeEnvironment.ServerErrorFor(wrong));
    }
}
