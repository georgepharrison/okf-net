namespace Okf.Core.Tests.Lint;

/// <summary>Configuration file parsing (PRD CLI-4, CLI-6).</summary>
public class OkfConfigTests
{
    [Fact]
    public void ReadsSeveritiesTreatAllWarningsTagRegistryAndVerifyActor()
    {
        var config = OkfConfig.Parse(
            """
            {
              "lint": {
                "severities": { "OKF0301": "error", "OKF0302": "hidden" },
                "treatAllWarningsAsErrors": true,
                "tagRegistry": ["finance", "revenue"]
              },
              "verify": { "actor": "human:ringo" }
            }
            """,
            "test");

        Assert.Equal(OkfSeverity.Error, config.Severities.Severities[OkfRules.MissingDescription]);
        Assert.Equal(OkfSeverity.Hidden, config.Severities.Severities[OkfRules.BrokenInternalLink]);
        Assert.True(config.Severities.TreatAllWarningsAsErrors);
        Assert.Equal(["finance", "revenue"], config.TagRegistry);
        Assert.Equal("human:ringo", config.VerifyActor);
    }

    /// <summary>
    /// An okf.json a user created and has not filled in yet is a file with no settings,
    /// not a malformed one: PRD CLI-6 only refuses what it cannot understand.
    /// </summary>
    /// <param name="json">The file's whole content.</param>
    [Theory]
    [InlineData("")]
    [InlineData("   \n\t\n")]
    public void AFileWithNothingInItContributesNothing(string json)
    {
        var config = OkfConfig.Parse(json, "test");

        Assert.True(config.Severities.IsEmpty);
        Assert.Null(config.SearchScope);
        Assert.Null(config.AutoRegister);
    }

    [Fact]
    public void AnEmptyOrUnrelatedFileContributesNothing()
    {
        var config = OkfConfig.Parse("""{ "somethingOkfDoesNotModel": { "yet": true } }""", "test");

        Assert.True(config.Severities.IsEmpty);
        Assert.Null(config.TagRegistry);
        Assert.Null(config.VerifyActor);
    }

    /// <summary>
    /// <c>search.scope</c> is readable in either layer, and its four values are exactly the
    /// four <c>--scope</c> accepts, so a setting and a flag can never mean different things
    /// (PRD CLI-3).
    /// </summary>
    /// <param name="written">The value as written in the file.</param>
    /// <param name="expected">The scope it parses to.</param>
    [Theory]
    [InlineData("project", OkfScopeKind.Project)]
    [InlineData("personal", OkfScopeKind.Personal)]
    [InlineData("registered", OkfScopeKind.Registered)]
    [InlineData("all", OkfScopeKind.All)]
    public void ReadsSearchScope(string written, OkfScopeKind expected)
    {
        var config = OkfConfig.Parse($$"""{ "search": { "scope": "{{written}}" } }""", "test");

        Assert.Equal(expected, config.SearchScope);
    }

    /// <summary>A typo must not silently fall back to the default scope.</summary>
    /// <param name="json">The configuration text.</param>
    [Theory]
    [InlineData("""{ "search": { "scope": "everything" } }""")]
    [InlineData("""{ "search": { "scope": true } }""")]
    [InlineData("""{ "search": "all" }""")]
    public void AnUnknownSearchScopeIsRefusedRatherThanIgnored(string json)
    {
        var exception = Assert.Throws<OkfConfigException>(() => OkfConfig.Parse(json, "test"));

        Assert.Contains("search", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// <c>autoRegister</c> is recorded and type-checked, and only in the global layer: a
    /// committed project file that could turn it on would let a clone write to a
    /// contributor's machine-wide registry (decisions.md §6).
    /// </summary>
    [Fact]
    public void AutoRegisterIsAGlobalOnlySettingAndIsRefusedInAProjectFile()
    {
        Assert.False(OkfConfig.Parse("""{ "autoRegister": false }""", "test", globalLayer: true).AutoRegister);
        Assert.True(OkfConfig.Parse("""{ "autoRegister": true }""", "test", globalLayer: true).AutoRegister);
        Assert.Null(OkfConfig.Parse("""{ "lint": {} }""", "test", globalLayer: true).AutoRegister);

        var refused = Assert.Throws<OkfConfigException>(
            () => OkfConfig.Parse("""{ "autoRegister": true }""", "test"));
        Assert.Contains("global setting", refused.Message, StringComparison.Ordinal);

        var mistyped = Assert.Throws<OkfConfigException>(
            () => OkfConfig.Parse("""{ "autoRegister": "yes" }""", "test", globalLayer: true));
        Assert.Contains("boolean", mistyped.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CommentsAndTrailingCommasAreTolerated()
    {
        var config = OkfConfig.Parse(
            """
            {
              // okf-net's own house rules
              "lint": { "severities": { "OKF0304": "warning", }, }
            }
            """,
            "test");

        Assert.Equal(OkfSeverity.Warning, config.Severities.Severities[OkfRules.MissingTags]);
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("[]")]
    [InlineData("""{ "lint": 3 }""")]
    [InlineData("""{ "lint": { "severities": [] } }""")]
    [InlineData("""{ "lint": { "severities": { "OKF0301": "shout" } } }""")]
    [InlineData("""{ "lint": { "treatAllWarningsAsErrors": "yes" } }""")]
    [InlineData("""{ "lint": { "tagRegistry": "finance" } }""")]
    [InlineData("""{ "verify": { "actor": 3 } }""")]
    [InlineData("""{ "verify": 3 }""")]
    [InlineData("""{ "verify": [] }""")]
    public void MalformedConfigurationIsRejected(string json) =>
        Assert.Throws<OkfConfigException>(() => OkfConfig.Parse(json, "test"));

    /// <summary>
    /// The message is the whole value of a config error: <c>okf</c> prints it and exits, so
    /// one that does not name the key it rejected, and the file it was in, leaves the reader
    /// to find it themselves. <see cref="MalformedConfigurationIsRejected" /> only proves the
    /// throw; every message below was reachable with no assertion on its text at all until
    /// this test (the #14 mutation run found each one by blanking it and losing nothing).
    /// </summary>
    /// <param name="json">The configuration text.</param>
    /// <param name="expected">The phrase the message must carry.</param>
    [Theory]
    [InlineData("[]", "must contain a JSON object at its root")]
    [InlineData("""{ "lint": 3 }""", "`lint` must be an object")]
    [InlineData("""{ "lint": { "severities": [] } }""", "`lint.severities` must be an object")]
    [InlineData("""{ "lint": { "severities": { "OKF0301": "shout" } } }""", "`lint.severities.OKF0301` must be one of")]
    [InlineData(
        """{ "lint": { "treatAllWarningsAsErrors": "yes" } }""",
        "`lint.treatAllWarningsAsErrors` must be a boolean")]
    [InlineData("""{ "lint": { "tagRegistry": "finance" } }""", "`lint.tagRegistry` must be an array of strings")]
    [InlineData(
        """{ "lint": { "tagRegistry": ["finance", 3] } }""",
        "`lint.tagRegistry` must be an array of strings")]
    [InlineData("""{ "verify": 3 }""", "`verify` must be an object")]
    [InlineData("""{ "verify": { "actor": 3 } }""", "`verify.actor` must be a string")]
    public void ARejectionNamesTheKeyItRejectedAndTheFileItWasIn(string json, string expected)
    {
        var rejected = Assert.Throws<OkfConfigException>(() => OkfConfig.Parse(json, "project/okf.json"));

        Assert.Contains(expected, rejected.Message, StringComparison.Ordinal);
        Assert.Contains("project/okf.json", rejected.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnknownRuleIdSurvivesParsingAndIsCaughtByTheResolver()
    {
        // The config layer records what it was given; rejecting unknown ids is the
        // resolver's job, so one message can name every offending layer at once.
        var config = OkfConfig.Parse("""{ "lint": { "severities": { "OKF9999": "error" } } }""", "test");

        Assert.True(config.Severities.Severities.ContainsKey("OKF9999"));
        Assert.Throws<OkfConfigException>(() => new OkfSeverityResolver([config.Severities]));
    }

    [Fact]
    public void AMissingFileIsNotAnError()
    {
        var path = Path.Combine(Path.GetTempPath(), "okf-tests", Path.GetRandomFileName(), "okf.json");

        Assert.Null(OkfConfig.TryLoad(path));
    }

    [Fact]
    public void AFileOnDiskIsReadAndNamedAsItsOwnSource()
    {
        using var bundle = new TempBundle();
        var path = Path.Combine(bundle.Root, "okf.json");
        File.WriteAllText(path, """{ "lint": { "severities": { "OKF0302": "error" } } }""");

        var config = OkfConfig.TryLoad(path);

        Assert.NotNull(config);
        Assert.Equal(path, config.Source);
        Assert.Equal(path, config.Severities.Name);
    }
}
