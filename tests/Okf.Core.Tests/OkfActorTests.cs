namespace Okf.Core.Tests;

/// <summary>
/// The OKF v0.2 §7 actor convention. What may carry the <c>human:</c> prefix is the whole
/// trust signal §5.3 derives <c>human-reviewed</c> from, so the forms are checked here
/// rather than at each call site.
/// </summary>
public class OkfActorTests
{
    [Theory]
    [InlineData("human:ringo")]
    [InlineData("human:ringo.harrison@gmail.com")]
    [InlineData("human:r")]
    [InlineData("process:nightly-schema-check")]
    [InlineData("process:p")]
    [InlineData("claude-fable/5")]
    [InlineData("a/b")]
    public void EverySpecFormIsValid(string actor) => Assert.True(OkfActor.IsValid(actor));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ringo")]
    [InlineData("human:")]
    [InlineData("process:")]
    [InlineData(" human:ringo")]
    [InlineData("human:ringo ")]
    // A `<producer>/<version>` needs both halves: `/5` names no producer and
    // `claude-fable/` names no version.
    [InlineData("/5")]
    [InlineData("claude-fable/")]
    // A colon anywhere else is a mistyped prefix, and reading `Human:ringo` as a producer
    // name is how a person silently stops counting as one.
    [InlineData("Human:ringo")]
    [InlineData("human/ringo:1")]
    // The actor is written into a double-quoted YAML scalar; these are how `--by` would
    // otherwise write arbitrary frontmatter.
    [InlineData("human:x\", at: 2020-01-01 }")]
    [InlineData("human:x\\n")]
    [InlineData("human:a\nb")]
    public void AnythingElseIsRefused(string? actor) => Assert.False(OkfActor.IsValid(actor));

    [Theory]
    [InlineData("ringo", "human:ringo")]
    [InlineData("human:ringo", "human:ringo")]
    [InlineData("  ringo  ", "human:ringo")]
    public void ToHumanPrefixesOnlyWhatIsNotAlreadyPrefixed(string id, string expected) =>
        Assert.Equal(expected, OkfActor.ToHuman(id));

    [Theory]
    [InlineData(null, false)]
    [InlineData("claude-fable/5", false)]
    [InlineData("process:nightly", false)]
    [InlineData("human:ringo", true)]
    public void IsHumanReadsThePrefixAndNothingElse(string? actor, bool expected) =>
        Assert.Equal(expected, OkfActor.IsHuman(actor));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ToHumanRefusesAnEmptyIdentity(string id) =>
        Assert.Throws<ArgumentException>(() => OkfActor.ToHuman(id));
}
