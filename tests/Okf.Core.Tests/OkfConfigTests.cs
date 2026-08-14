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
        var config = OkfConfig.Parse("""{ "search": { "scope": "project" } }""", "test");

        Assert.True(config.Severities.IsEmpty);
        Assert.Null(config.TagRegistry);
        Assert.Null(config.VerifyActor);
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
