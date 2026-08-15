namespace Okf.Core.Tests;

/// <summary>
/// Reading one concept by id (PRD CORE-12) and the containment rule every path obeys
/// (PRD MCP-5).
/// </summary>
public class OkfConceptReaderTests
{
    [Fact]
    public void ReadingAConceptReturnsItsFrontmatterBodyAndTheDerivedJudgements()
    {
        using var bundle = Bundle();

        var result = OkfConceptReader.Read(
            bundle.Bundle,
            "topics/widgets.md",
            new OkfConceptOptions { Today = TempBundle.Today });

        Assert.Equal(OkfConceptStatus.Ok, result.Status);
        var concept = result.Concept!;

        Assert.Equal("topics/widgets", concept.Id);
        Assert.Equal("topics/widgets.md", concept.Path);
        Assert.Equal("Widgets", concept.Title);
        Assert.Equal("Reference", concept.Type);
        Assert.Equal("Everything about widgets.", concept.Description);
        Assert.Equal(["catalog", "widgets"], concept.Tags);
        Assert.Equal("The body, which the reader returns whole.", concept.Body);

        // Derived, never stored: a `human:` verifier is human-reviewed (§5.3), and the
        // 2020 expiry is stale against the fixed date (§5.5).
        Assert.Equal(OkfTrustTier.HumanReviewed, concept.TrustTier);
        Assert.True(concept.Stale);
    }

    [Fact]
    public void TheIdAndThePathAreTheSameRequest()
    {
        using var bundle = Bundle();

        var withSuffix = OkfConceptReader.Read(bundle.Bundle, "topics/widgets.md");
        var withoutSuffix = OkfConceptReader.Read(bundle.Bundle, "topics/widgets");

        Assert.True(withSuffix.IsOk);
        Assert.True(withoutSuffix.IsOk);
        Assert.Equal(withSuffix.Concept!.AbsolutePath, withoutSuffix.Concept!.AbsolutePath);
    }

    [Fact]
    public void TitleFallsBackToTheFilenameStemAndAbsentFieldsAreNull()
    {
        using var bundle = Bundle();
        bundle.Add("bare.md", "---\ntype: Note\n---\n\nNothing else.\n");

        var concept = OkfConceptReader.Read(bundle.Bundle, "bare").Concept!;

        Assert.Equal("bare", concept.Title);
        Assert.Null(concept.Description);
        Assert.Empty(concept.Tags);
        Assert.Equal(OkfTrustTier.Unverified, concept.TrustTier);
        Assert.False(concept.Stale);
    }

    [Theory]
    [InlineData("../outside.md")]
    [InlineData("topics/../../outside.md")]
    [InlineData("..")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("topics\\widgets.md")]
    public void APathThatIsNotBundleRelativeIsRefusedBeforeTheFilesystemIsTouched(string? id)
    {
        using var bundle = Bundle();
        File.WriteAllText(
            Path.Combine(Directory.GetParent(bundle.Root)!.FullName, "outside.md"),
            "---\ntype: Secret\n---\n\nNot in any bundle.\n");

        var result = OkfConceptReader.Read(bundle.Bundle, id);

        Assert.Equal(OkfConceptStatus.InvalidPath, result.Status);
        Assert.Null(result.Concept);
    }

    [Fact]
    public void AnAbsolutePathIsRefusedEvenWhenItPointsInsideTheBundle()
    {
        using var bundle = Bundle();
        var inside = Path.Combine(bundle.Root, "topics", "widgets.md");

        // Accepting one would make the caller's path grammar depend on where the bundle
        // happens to live on this machine.
        Assert.Equal(OkfConceptStatus.InvalidPath, OkfConceptReader.Read(bundle.Bundle, inside).Status);
    }

    [Fact]
    public void AMissingConceptIsAMissRatherThanAnError()
    {
        using var bundle = Bundle();

        var result = OkfConceptReader.Read(bundle.Bundle, "topics/sprockets");

        Assert.Equal(OkfConceptStatus.NotFound, result.Status);
        Assert.Contains("topics/sprockets.md", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AConceptWhoseFrontmatterDoesNotParseIsReportedAsSuchAndPointsAtLint()
    {
        using var bundle = Bundle();
        bundle.Add("broken.md", "---\ntype: [unterminated\n---\n\nBody.\n");

        var result = OkfConceptReader.Read(bundle.Bundle, "broken.md");

        Assert.Equal(OkfConceptStatus.Unparseable, result.Status);
        Assert.Contains("okf lint", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadingCanBeFedTextRatherThanTheFilesystem()
    {
        using var bundle = Bundle();

        var concept = OkfConceptReader.Read(
            bundle.Bundle,
            "topics/anything.md",
            new OkfConceptOptions { ReadText = _ => "---\ntype: Metric\ntitle: Injected\n---\n\nBody.\n" }).Concept!;

        Assert.Equal("Injected", concept.Title);
        Assert.Equal("Metric", concept.Type);
    }

    [Theory]
    [InlineData("topics", true)]
    [InlineData("topics/widgets.md", true)]
    [InlineData("", true)]
    [InlineData("..", false)]
    [InlineData("topics/../..", false)]
    [InlineData("/etc", false)]

    // A NUL is refused rather than passed to `Path.GetFullPath`, which throws on one. A
    // containment check that throws is one a caller can turn into a crash, and `okf mcp`
    // hands this method whatever text a client sent.
    [InlineData("topics/wid\0gets.md", false)]
    public void TryResolveConfinesEveryPathToTheBundleRoot(string relative, bool contained)
    {
        using var bundle = Bundle();

        Assert.Equal(contained, bundle.Bundle.TryResolve(relative, out var resolved));

        if (contained)
        {
            Assert.StartsWith(bundle.Bundle.Root, resolved!, StringComparison.Ordinal);
        }
        else
        {
            Assert.Null(resolved);
        }
    }

    private static TempBundle Bundle()
    {
        var bundle = new TempBundle();
        bundle.Add(
            "topics/widgets.md",
            """
            ---
            type: Reference
            title: Widgets
            description: Everything about widgets.
            tags: [catalog, widgets]
            stale_after: 2020-01-01
            verified: { by: human:tests@example.invalid, at: 2026-01-02T00:00:00Z }
            ---

            The body, which the reader returns whole.

            """);
        return bundle;
    }
}
