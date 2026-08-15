namespace Okf.Core.Tests;

/// <summary>
/// Severity resolution (PRD CLI-4, CLI-5, CLI-6): built-in defaults underneath, then each
/// configuration layer in precedence order.
/// </summary>
public class OkfSeverityResolverTests
{
    [Fact]
    public void OnlyConformanceRulesErrorByDefault()
    {
        foreach (var rule in OkfRules.All)
        {
            var expected = rule.Category == OkfRuleCategory.Conformance;
            Assert.Equal(expected, OkfSeverityResolver.Default.Resolve(rule.Id) == OkfSeverity.Error);
        }
    }

    [Theory]
    [InlineData(OkfRules.UnparseableFrontmatter, OkfSeverity.Error)]
    [InlineData(OkfRules.MissingType, OkfSeverity.Error)]
    [InlineData(OkfRules.InvalidIndexStructure, OkfSeverity.Error)]
    [InlineData(OkfRules.InvalidLogStructure, OkfSeverity.Error)]
    [InlineData(OkfRules.UncitedFootnote, OkfSeverity.Warning)]
    [InlineData(OkfRules.UnusedSourceId, OkfSeverity.Warning)]
    [InlineData(OkfRules.SourceDrift, OkfSeverity.Warning)]
    [InlineData(OkfRules.SelfVerification, OkfSeverity.Warning)]
    [InlineData(OkfRules.StaleConcept, OkfSeverity.Warning)]
    [InlineData(OkfRules.MissingDescription, OkfSeverity.Warning)]
    [InlineData(OkfRules.BrokenInternalLink, OkfSeverity.Info)]
    [InlineData(OkfRules.NearDuplicateConcept, OkfSeverity.Warning)]
    [InlineData(OkfRules.MissingTags, OkfSeverity.Hidden)]
    [InlineData(OkfRules.UnregisteredTag, OkfSeverity.Hidden)]
    public void DefaultsMatchTheDecidedTable(string ruleId, OkfSeverity expected)
    {
        Assert.Equal(expected, OkfSeverityResolver.Default.Resolve(ruleId));
        Assert.Equal(OkfSeverityResolver.DefaultsLayerName, OkfSeverityResolver.Default.ResolveWithSource(ruleId).Source);
    }

    [Fact]
    public void LaterLayersWinPerRule()
    {
        var lower = new OkfSeverityLayer("global");
        lower.Severities[OkfRules.MissingDescription] = OkfSeverity.Error;
        lower.Severities[OkfRules.BrokenInternalLink] = OkfSeverity.Warning;

        var upper = new OkfSeverityLayer("project");
        upper.Severities[OkfRules.MissingDescription] = OkfSeverity.Hidden;

        var resolver = new OkfSeverityResolver([lower, upper]);

        Assert.Equal(OkfSeverity.Hidden, resolver.Resolve(OkfRules.MissingDescription));
        Assert.Equal("project", resolver.ResolveWithSource(OkfRules.MissingDescription).Source);

        // A rule the upper layer is silent about keeps the lower layer's value.
        Assert.Equal(OkfSeverity.Warning, resolver.Resolve(OkfRules.BrokenInternalLink));
        Assert.Equal("global", resolver.ResolveWithSource(OkfRules.BrokenInternalLink).Source);
    }

    [Fact]
    public void TreatAllWarningsAsErrorsPromotesOnlyWhatIsCurrentlyAtWarning()
    {
        var layer = new OkfSeverityLayer("project") { TreatAllWarningsAsErrors = true };
        layer.Severities[OkfRules.StaleConcept] = OkfSeverity.Info;
        var resolver = new OkfSeverityResolver([layer]);

        Assert.Equal(OkfSeverity.Error, resolver.Resolve(OkfRules.MissingDescription));

        // Broken links default to info, so the blanket promotion does not touch them (Q5).
        Assert.Equal(OkfSeverity.Info, resolver.Resolve(OkfRules.BrokenInternalLink));

        // Neither does it re-promote a rule the consumer deliberately demoted.
        Assert.Equal(OkfSeverity.Info, resolver.Resolve(OkfRules.StaleConcept));

        // Nor does it touch what is already hidden.
        Assert.Equal(OkfSeverity.Hidden, resolver.Resolve(OkfRules.MissingTags));
        Assert.Contains("treatAllWarningsAsErrors", resolver.ResolveWithSource(OkfRules.MissingDescription).Source, StringComparison.Ordinal);
    }

    [Fact]
    public void ThePromotionNamesTheLayerThatTurnedItOnRatherThanTheDefaults()
    {
        // `global` speaks to the flag and loses, so naming the *winning* layer is the only
        // way to pass: a resolver that reported the first layer to mention it, or the
        // defaults, would name something the consumer cannot edit to undo the promotion.
        var global = new OkfSeverityLayer("global") { TreatAllWarningsAsErrors = false };
        var project = new OkfSeverityLayer("project") { TreatAllWarningsAsErrors = true };
        var resolver = new OkfSeverityResolver([global, project]);

        // PRD CLI-6: the source is what sends a consumer to the file they can edit, so it
        // has to be the layer that set the flag — never the built-in defaults, which set
        // nothing and cannot be edited.
        Assert.Equal("project", resolver.TreatAllWarningsAsErrorsSource);
        Assert.NotEqual(OkfSeverityResolver.DefaultsLayerName, resolver.TreatAllWarningsAsErrorsSource);
        Assert.Equal(
            "project (treatAllWarningsAsErrors)",
            resolver.ResolveWithSource(OkfRules.MissingDescription).Source);
    }

    [Fact]
    public void ALaterLayerCanTurnTreatAllWarningsAsErrorsBackOff()
    {
        var global = new OkfSeverityLayer("global") { TreatAllWarningsAsErrors = true };
        var project = new OkfSeverityLayer("project") { TreatAllWarningsAsErrors = false };

        Assert.False(new OkfSeverityResolver([global, project]).TreatAllWarningsAsErrors);
        Assert.True(new OkfSeverityResolver([project, global]).TreatAllWarningsAsErrors);
    }

    [Fact]
    public void AnUnknownRuleIdIsRejectedRatherThanIgnored()
    {
        var layer = new OkfSeverityLayer("project config");
        layer.Severities["OKF9999"] = OkfSeverity.Error;

        var exception = Assert.Throws<OkfConfigException>(() => new OkfSeverityResolver([layer]));

        Assert.Contains("OKF9999", exception.Message, StringComparison.Ordinal);
        Assert.Contains("project config", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryRuleIdMatchesItsCategoryRange()
    {
        foreach (var rule in OkfRules.All)
        {
            var number = int.Parse(rule.Id.AsSpan("OKF".Length), System.Globalization.CultureInfo.InvariantCulture);
            var expected = rule.Category switch
            {
                OkfRuleCategory.Conformance => 0,
                OkfRuleCategory.Provenance => 1,
                OkfRuleCategory.Trust => 2,
                OkfRuleCategory.Hygiene => 3,
                _ => -1,
            };

            Assert.StartsWith("OKF", rule.Id, StringComparison.Ordinal);
            Assert.Equal(7, rule.Id.Length);
            Assert.Equal(expected, number / 100);
        }
    }

    [Theory]
    [InlineData("error", OkfSeverity.Error)]
    [InlineData("Warning", OkfSeverity.Warning)]
    [InlineData("info", OkfSeverity.Info)]
    [InlineData("hidden", OkfSeverity.Hidden)]
    [InlineData("none", OkfSeverity.Hidden)]
    public void SeverityTokensParse(string text, OkfSeverity expected)
    {
        Assert.True(OkfSeverityExtensions.TryParse(text, out var severity));
        Assert.Equal(expected, severity);
    }

    [Fact]
    public void AnUnknownSeverityTokenDoesNotParse() =>
        Assert.False(OkfSeverityExtensions.TryParse("loud", out _));
}
