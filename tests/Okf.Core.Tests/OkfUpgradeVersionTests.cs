using Okf.Core;

namespace Okf.Core.Tests;

/// <summary>
/// Semver precedence (work item #23).
/// </summary>
/// <remarks>
/// Every expected value here is read off <b>semver 2.0.0 §10 and §11</b>, not off the
/// implementation: the ordering in <see cref="Precedence" /> is the spec's own worked
/// example, character for character, and each rule case names the clause it comes from.
/// That is AD-44's independent-oracle half — the table is right or the spec is.
/// </remarks>
public sealed class OkfUpgradeVersionTests
{
    /// <summary>
    /// §11's worked example: "1.0.0-alpha &lt; 1.0.0-alpha.1 &lt; 1.0.0-alpha.beta &lt;
    /// 1.0.0-beta &lt; 1.0.0-beta.2 &lt; 1.0.0-beta.11 &lt; 1.0.0-rc.1 &lt; 1.0.0".
    /// </summary>
    private static readonly string[] Precedence =
    [
        "1.0.0-alpha",
        "1.0.0-alpha.1",
        "1.0.0-alpha.beta",
        "1.0.0-beta",
        "1.0.0-beta.2",
        "1.0.0-beta.11",
        "1.0.0-rc.1",
        "1.0.0",
    ];

    [Fact]
    public void OrdersTheSpecsWorkedExample()
    {
        for (var index = 0; index + 1 < Precedence.Length; index++)
        {
            var lower = Precedence[index];
            var higher = Precedence[index + 1];
            Assert.True(
                OkfUpgradeVersion.Compare(lower, higher) < 0,
                $"expected {lower} to precede {higher}");
            Assert.True(
                OkfUpgradeVersion.Compare(higher, lower) > 0,
                $"expected {higher} to follow {lower}");
        }
    }

    [Theory]
    // §11.2: major, then minor, then patch, compared numerically.
    [InlineData("1.0.0", "2.0.0", -1)]
    [InlineData("2.1.0", "2.1.1", -1)]
    [InlineData("2.9.0", "2.10.0", -1)]
    [InlineData("1.0.0", "1.0.0", 0)]
    // §11.3: a prerelease has LOWER precedence than the normal version it precedes. This is
    // the case that decides whether `1.1.0-rc.1` reads as an upgrade over `1.1.0`.
    [InlineData("1.1.0-rc.1", "1.1.0", -1)]
    [InlineData("1.1.0", "1.1.0-rc.1", 1)]
    // §11.4.1: numeric identifiers compare numerically, so rc.10 follows rc.9. The bare-
    // numeric rows put the numeric identifier FIRST, where nothing separates it from the
    // core, so a prerelease split that kept a character of the core would compare them as
    // text and read 1.0.0-10 as the lower of the two.
    [InlineData("1.0.0-rc.9", "1.0.0-rc.10", -1)]
    [InlineData("1.0.0-rc.2", "1.0.0-rc.11", -1)]
    [InlineData("1.0.0-9", "1.0.0-10", -1)]
    [InlineData("1.0.0-2", "1.0.0-11", -1)]
    // §2: the core is exactly three components. A fourth is not part of precedence, and a
    // missing one reads as zero.
    [InlineData("1.0.0.9", "1.0.0.1", 0)]
    [InlineData("1.0", "1.0.0", 0)]
    [InlineData("1", "1.0.0", 0)]
    [InlineData("1.1", "1.0.0", 1)]
    // §11.4.3: numeric identifiers always have lower precedence than alphanumeric ones.
    [InlineData("1.0.0-1", "1.0.0-alpha", -1)]
    // §11.4.4: with everything before them equal, more fields wins.
    [InlineData("1.0.0-rc.1", "1.0.0-rc.1.1", -1)]
    // §10: build metadata is ignored when determining precedence.
    [InlineData("1.0.0+abc1234", "1.0.0", 0)]
    [InlineData("1.0.0+aaa", "1.0.0+zzz", 0)]
    [InlineData("1.0.0-rc.1+aaa", "1.0.0-rc.1", 0)]
    public void ComparesBySemverPrecedence(string left, string right, int expected)
    {
        Assert.Equal(expected, Math.Sign(OkfUpgradeVersion.Compare(left, right)));
        Assert.Equal(-expected, Math.Sign(OkfUpgradeVersion.Compare(right, left)));
    }

    [Theory]
    [InlineData("v1.0.0", "1.0.0")]
    [InlineData("v1.1.0-rc.1", "1.1.0-rc.1")]
    public void IgnoresATagsLeadingV(string tagged, string bare) =>
        Assert.Equal(0, OkfUpgradeVersion.Compare(tagged, bare));

    /// <summary>
    /// AD-41: the unstamped default is `0.0.0-dev`, and a build carrying it was built from a
    /// working tree rather than a tag. Comparing its version string to a release's would be
    /// answering a question about strings; it is declared upgradable instead.
    /// </summary>
    [Theory]
    [InlineData("0.0.0-dev", true)]
    [InlineData("0.0.0-dev+9765a305", true)]
    [InlineData("0.0.0", true)]
    [InlineData("1.0.0", false)]
    [InlineData("1.1.0-rc.1", false)]
    public void RecognizesAnUnstampedBuild(string version, bool expected) =>
        Assert.Equal(expected, OkfUpgradeVersion.IsDevelopmentBuild(version));

    /// <summary>
    /// The core is whatever precedes the first <c>-</c> or <c>+</c>, even when that is
    /// nothing at all. A release manifest is downloaded (AD-53), so its <c>version</c> is
    /// external input and every one of these has to land somewhere rather than throw; the
    /// somewhere is the <c>0.0.0</c> core, which AD-41 already treats as unstamped.
    /// </summary>
    [Theory]
    [InlineData("+1.2.3", true)]
    [InlineData("-1.2.3", true)]
    [InlineData("", true)]
    [InlineData("dev", true)]
    [InlineData("1.2.3+dev", false)]
    public void ReadsADegenerateVersionAsAZeroCore(string version, bool expected) =>
        Assert.Equal(expected, OkfUpgradeVersion.IsDevelopmentBuild(version));

    [Theory]
    [InlineData("1.0.0", "1.1.0-rc.1", true)]
    [InlineData("1.1.0-rc.1", "1.1.0", true)]
    [InlineData("1.1.0", "1.1.0", false)]
    [InlineData("1.1.0", "1.1.0-rc.1", false)]
    [InlineData("1.1.0", "1.0.0", false)]
    // A local build takes any release, including one that sorts below its own 0.0.0 core.
    [InlineData("0.0.0-dev+9765a305", "1.0.0", true)]
    [InlineData("0.0.0-dev", "0.0.1", true)]
    // Nothing to compare against is not an upgrade — not even for an unstamped build, which
    // takes every release it is offered but cannot be offered a nameless one.
    [InlineData("1.0.0", "", false)]
    [InlineData("0.0.0-dev", "", false)]
    [InlineData("0.0.0-dev", "1.0.0", true)]
    public void AnswersWhetherAnUpgradeIsAvailable(string current, string available, bool expected) =>
        Assert.Equal(expected, OkfUpgradeVersion.IsUpgradeAvailable(current, available));
}
