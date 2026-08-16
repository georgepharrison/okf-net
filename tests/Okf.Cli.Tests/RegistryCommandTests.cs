using System.Text.Json;
using Okf.Core;

namespace Okf.Cli.Tests;

/// <summary>
/// <c>okf register</c>, <c>okf unregister</c> and <c>okf registry</c> (PRD CLI-2). Every
/// run here writes into a temporary home: no test may touch the developer's own registry.
/// </summary>
public class RegistryCommandTests
{
    [Fact]
    public void RegisteringIsIdempotentAndWritesTheRegistryOnce()
    {
        using var home = new TempTree();
        var project = home.CreateDirectory("widgets");
        Cli.RunIn(project, home.Root, "init");

        var first = Cli.RunIn(project, home.Root, "register");
        var registry = RegistryPath(home);
        var written = File.ReadAllText(registry);
        var second = Cli.RunIn(project, home.Root, "register");

        Assert.Equal(CliApplication.ExitSuccess, first.ExitCode);
        Assert.Contains("Registered 'widgets' (vault)", first.Output, StringComparison.Ordinal);
        Assert.Equal(CliApplication.ExitSuccess, second.ExitCode);
        Assert.Contains("Already registered as 'widgets'", second.Output, StringComparison.Ordinal);
        Assert.Equal(written, File.ReadAllText(registry));
        Assert.Single(Entries(home));
    }

    /// <summary>
    /// `okf register` with no path takes CLI-1's target: the project vault when there is
    /// one, the personal vault when there is not. The personal vault becomes an ordinary
    /// entry — the kind and the id are the same shape as any other.
    /// </summary>
    [Fact]
    public void WithNoProjectVaultRegisterTargetsThePersonalVault()
    {
        using var home = new TempTree();
        using var elsewhere = new TempTree();
        Cli.RunIn(home.Root, home.Root, "init", "--personal");

        var run = Cli.RunIn(elsewhere.Root, home.Root, "register");

        Assert.Equal(CliApplication.ExitSuccess, run.ExitCode);
        var entry = Assert.Single(Entries(home));
        Assert.Equal("vault", entry.GetProperty("kind").GetString());
        Assert.Equal(Path.Combine(home.Root, "okf"), entry.GetProperty("path").GetString());
    }

    [Fact]
    public void ABareBundleRegistersAsABundle()
    {
        using var home = new TempTree();
        var bundle = home.CopyFixture("conformant", "vendor/widgets");

        var run = Cli.RunIn(home.Root, home.Root, "register", bundle);

        Assert.Equal(CliApplication.ExitSuccess, run.ExitCode);
        var entry = Assert.Single(Entries(home));
        Assert.Equal("bundle", entry.GetProperty("kind").GetString());
        Assert.Equal(bundle, entry.GetProperty("path").GetString());
    }

    [Fact]
    public void RegisteringSomethingThatIsNotThereIsTheUsageExitCodeAndWritesNothing()
    {
        using var home = new TempTree();

        var run = Cli.RunIn(home.Root, home.Root, "register", "nowhere");

        Assert.Equal(CliApplication.ExitUsage, run.ExitCode);
        Assert.Contains("No such directory", run.Error, StringComparison.Ordinal);
        Assert.False(File.Exists(RegistryPath(home)));
    }

    [Fact]
    public void UnregisteringWorksByPathOrIdAndAnUnknownOneSucceeds()
    {
        using var home = new TempTree();
        var project = home.CreateDirectory("widgets");
        Cli.RunIn(project, home.Root, "init");
        Cli.RunIn(project, home.Root, "register");

        var unknown = Cli.RunIn(home.Root, home.Root, "unregister", "never-registered");
        var byId = Cli.RunIn(home.Root, home.Root, "unregister", "widgets");

        Assert.Equal(CliApplication.ExitSuccess, unknown.ExitCode);
        Assert.Contains("Nothing to unregister", unknown.Output, StringComparison.Ordinal);
        Assert.Equal(CliApplication.ExitSuccess, byId.ExitCode);
        Assert.Contains("Unregistered 'widgets'", byId.Output, StringComparison.Ordinal);
        Assert.Empty(Entries(home));
        Assert.True(Directory.Exists(Path.Combine(project, "okf")), "unregistering never touches the directory");
    }

    [Fact]
    public void ListingReportsIdKindPathAndWhetherThePathIsStillThere()
    {
        using var home = new TempTree();
        var project = home.CreateDirectory("widgets");
        Cli.RunIn(project, home.Root, "init");
        Cli.RunIn(project, home.Root, "register");
        var gone = home.CopyFixture("conformant", "vendor/ghost");
        Cli.RunIn(home.Root, home.Root, "register", gone);
        Directory.Delete(gone, recursive: true);

        var run = Cli.RunIn(home.Root, home.Root, "registry", "list");

        Assert.Equal(CliApplication.ExitSuccess, run.ExitCode);
        Assert.Contains(run.OutputLines, line => line.StartsWith("ghost", StringComparison.Ordinal) && line.EndsWith("(missing)", StringComparison.Ordinal));
        Assert.Contains(run.OutputLines, line => line.StartsWith("widgets", StringComparison.Ordinal) && !line.Contains("(missing)", StringComparison.Ordinal));
        Assert.Contains("2 entries", run.Output, StringComparison.Ordinal);
        Assert.Contains("1 path missing", run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void AnEmptyRegistrySaysSoAndSucceeds()
    {
        using var home = new TempTree();

        var run = Cli.RunIn(home.Root, home.Root, "registry", "list");

        Assert.Equal(CliApplication.ExitSuccess, run.ExitCode);
        Assert.Contains("No entries", run.Output, StringComparison.Ordinal);
        Assert.Contains("okf register", run.Output, StringComparison.Ordinal);
    }

    /// <summary>
    /// Pruning is the only registry command that writes without being told a path, and it
    /// writes only the registry: the directories are already gone and nothing under a vault
    /// is touched.
    /// </summary>
    [Fact]
    public void PruningRemovesOnlyMissingEntries()
    {
        using var home = new TempTree();
        var project = home.CreateDirectory("widgets");
        Cli.RunIn(project, home.Root, "init");
        Cli.RunIn(project, home.Root, "register");
        var gone = home.CopyFixture("conformant", "vendor/ghost");
        Cli.RunIn(home.Root, home.Root, "register", gone);
        Directory.Delete(gone, recursive: true);

        var pruned = Cli.RunIn(home.Root, home.Root, "registry", "prune");
        var again = Cli.RunIn(home.Root, home.Root, "registry", "prune");

        Assert.Equal(CliApplication.ExitSuccess, pruned.ExitCode);
        Assert.Contains("Pruned 'ghost'", pruned.Output, StringComparison.Ordinal);
        Assert.Contains("1 entry left", pruned.Output, StringComparison.Ordinal);
        Assert.Equal(["widgets"], Entries(home).Select(entry => entry.GetProperty("id").GetString()));
        Assert.Contains("Nothing to prune", again.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void ListJsonCarriesTheFieldsTheTextFormReports()
    {
        using var home = new TempTree();
        var project = home.CreateDirectory("widgets");
        Cli.RunIn(project, home.Root, "init");
        Cli.RunIn(project, home.Root, "register");

        var entry = Assert.Single(Entries(home));

        Assert.Equal("widgets", entry.GetProperty("id").GetString());
        Assert.Equal("vault", entry.GetProperty("kind").GetString());
        Assert.Equal(Path.Combine(project, "okf"), entry.GetProperty("path").GetString());
        Assert.True(entry.GetProperty("exists").GetBoolean());
        Assert.True(OkfCanonicalTimestamp.IsCanonical(entry.GetProperty("registeredAt").GetString()));
    }

    [Fact]
    public void AnUnknownRegistrySubcommandIsAUsageFailure()
    {
        using var home = new TempTree();

        var run = Cli.RunIn(home.Root, home.Root, "registry", "forget");

        Assert.Equal(CliApplication.ExitUsage, run.ExitCode);
        Assert.Contains("expected 'list' or 'prune'", run.Error, StringComparison.Ordinal);
    }

    /// <summary>
    /// `autoRegister` is accepted, validated and reported, and nothing acts on it: no code
    /// path registers anything without being asked (PRD CLI-2).
    /// </summary>
    [Fact]
    public void AutoRegisterIsRecordedAndReportedButRegistersNothing()
    {
        using var home = new TempTree();
        home.Write(Path.Combine(".config", "okf", "okf.json"), """{ "autoRegister": true }""");
        var project = home.CreateDirectory("widgets");
        Cli.RunIn(project, home.Root, "init");

        var listed = Cli.RunIn(project, home.Root, "registry", "list", "--verbose");

        Assert.Equal(CliApplication.ExitSuccess, listed.ExitCode);
        Assert.Contains("autoRegister true", listed.Error, StringComparison.Ordinal);
        Assert.Contains("nothing auto-registers", listed.Error, StringComparison.Ordinal);
        Assert.Contains("No entries", listed.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void AutoRegisterInAProjectConfigIsRefusedByTheCommandsThatReadConfiguration()
    {
        using var home = new TempTree();
        var project = home.CreateDirectory("widgets");
        Cli.RunIn(project, home.Root, "init");
        File.WriteAllText(Path.Combine(project, "okf", "okf.json"), """{ "autoRegister": true }""");

        var run = Cli.RunIn(project, home.Root, "lint");

        Assert.Equal(CliApplication.ExitUsage, run.ExitCode);
        Assert.Contains("global setting", run.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryRegistryVerbHasHelpAndTheVerbListNamesThem()
    {
        using var home = new TempTree();

        foreach (var verb in (string[])["register", "unregister", "registry"])
        {
            var help = Cli.RunIn(home.Root, home.Root, verb, "--help");

            Assert.Equal(CliApplication.ExitSuccess, help.ExitCode);
            Assert.Contains($"okf {verb}", help.Output, StringComparison.Ordinal);
        }

        var usage = Cli.RunIn(home.Root, home.Root, "help");

        Assert.Contains("okf register", usage.Output, StringComparison.Ordinal);
        Assert.Contains("okf unregister", usage.Output, StringComparison.Ordinal);
        Assert.Contains("okf registry", usage.Output, StringComparison.Ordinal);
    }

    private static string RegistryPath(TempTree home) =>
        Path.Combine(home.Root, ".config", "okf", OkfRegistry.FileName);

    private static IReadOnlyList<JsonElement> Entries(TempTree home)
    {
        var run = Cli.RunIn(home.Root, home.Root, "registry", "list", "--json");
        Assert.Equal(CliApplication.ExitSuccess, run.ExitCode);
        using var document = JsonDocument.Parse(run.Output);
        return [.. document.RootElement.EnumerateArray().Select(entry => entry.Clone())];
    }
}
