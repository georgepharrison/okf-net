namespace Okf.Core.Tests;

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
    public void MalformedConfigurationIsRejected(string json) =>
        Assert.Throws<OkfConfigException>(() => OkfConfig.Parse(json, "test"));

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
