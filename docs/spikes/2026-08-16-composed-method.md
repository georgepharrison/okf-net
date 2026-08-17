# Composed method: the solution-wide inventory

Issue #14, polish step H. Dated 2026-08-16, measured on `14-core-documents-lint`
off `dev` at `73c91ae`. Uncle Bob's "extract till you drop" and Kent Beck's
Composed Method are the bar: a public method should read as a chain of
well-named private methods, ~20 lines each.

This file is the shared worksheet for the lane split. The inventory below is the
**before** picture of all of `src/`, measured once; each lane appends its own
area's before/after underneath.

## How the numbers were measured

A ~180-line Python scanner, not Roslyn, so **the counts are approximate**. It
strips comments and string literals, tracks brace depth, and treats a line that
closes a parameter list and opens a block as a method body. A method's length is
the number of lines strictly between its opening `{` and its closing `}` —
blank lines and comments included, signature and braces excluded. So a method
reported as 20 occupies 22 lines of the file.

What it deliberately does not count: expression-bodied members (`=>`, always one
line), local functions and lambdas nested inside a method body (only members
declared directly on a type are counted), and type declarations with primary
constructors, which look exactly like a method signature followed by a block.
Constructors *are* counted — they are methods, and a long one is the same smell.

The scanner lives in the scratchpad rather than in the repo: it exists to rank
files for one work item, and a heuristic C# parser that nothing gates on is not
a tool the repo should carry.

It also has a known blind spot, found on review: it silently skips some members
in files whose bodies carry brace-bearing string literals — interpolated holes
and embedded JSON — because those unbalance its brace depth. Seven rows below
were re-measured by hand against `dev` at `73c91ae` and corrected; the count
column is therefore a **floor**, not a total, and a file's real longest method
may be one the scanner never saw. Read the ranking as "at least this much work
here".

## Inventory: `src/`, before

93 files scanned, 20 of them already clean. **at least 216 methods over 20
lines**, and the longest single method in the tree is `CompletionCommand.Zsh` at
154 lines.

| File | Methods > 20 lines | Longest method |
| --- | --- | --- |
| `src/Okf.Core/Lint/OkfLinter.cs` | 12 | `CheckHygiene` (88) |
| `src/Okf.Core/Site/OkfSiteHtml.cs` | 9 | `Landing` (87) |
| `src/Okf.Cli/Mcp/McpToolset.cs` | 9 | `ListTool` (58) |
| `src/Okf.Core/Bundle/OkfBundler.cs` | 8 | `Verify` (92) |
| `src/Okf.Core/Site/OkfSiteBuilder.cs` | 7 | `Build` (125) |
| `src/Okf.Core/Upgrade/OkfUpgrade.cs` | 7 | `Apply` (75) |
| `src/Okf.Core/Capture/OkfCaptureWriter.cs` | 7 | `Describe` (73) |
| `src/Okf.Core/Index/OkfIndex.cs` | 6 | `Plan` (143) |
| `src/Okf.Cli/Capture/CaptureCommand.cs` | 6 | `Close` (62) |
| `src/Okf.Core/Vault/OkfRegistry.cs` | 6 | `Parse` (54) |
| `src/Okf.Cli/Completion/CompletionCommand.cs` | 5 | `Zsh` (154) |
| `src/Okf.Core/Trust/OkfStamp.cs` | 5 | `TryInsert` (99) |
| `src/Okf.Core/Search/OkfSearch.cs` | 5 | `Search` (78) |
| `src/Okf.Cli/Mcp/McpServer.cs` | 5 | `Handle` (62) |
| `src/Okf.Cli/Index/IndexCommand.cs` | 5 | `Index` (43) |
| `src/Okf.Cli/Registry/RegistryCommand.cs` | 4 | `WriteUsage` (84) |
| `src/Okf.Cli/Site/SiteCommand.cs` | 4 | `Generate` (68) |
| `src/Okf.Cli/Search/SearchCommand.cs` | 4 | `Search` (66) |
| `src/Okf.Cli/Bundle/BundleCommand.cs` | 4 | `Package` (54) |
| `src/Okf.Core/Trust/OkfInbox.cs` | 4 | `Classify` (46) |
| `src/Okf.Cli/Inbox/InboxCommand.cs` | 4 | `WriteText` (41) |
| `src/Okf.Core/Vault/OkfScaffold.cs` | 4 | `Initialize` (40) |
| `src/Okf.Cli/Verify/VerifyCommand.cs` | 3 | `Verify` (104) |
| `src/Okf.Cli/Generated/GeneratedCommand.cs` | 3 | `Stamp` (95) |
| `src/Okf.Cli/Init/InitCommand.cs` | 3 | `Initialize` (81) |
| `src/Okf.Cli/Lint/LintCommand.cs` | 3 | `Lint` (72) |
| `src/Okf.Core/Site/OkfSiteGenerator.cs` | 3 | `MultiPage` (66) |
| `src/Okf.Cli/Skills/SkillsCommand.cs` | 3 | `Install` (51) |
| `src/Okf.Core/Documents/YamlBridge.cs` | 3 | `EmitValue` (48) |
| `src/Okf.Core/Vault/OkfAgentPointer.cs` | 3 | `WriteAgentsMd` (41) |
| `src/Okf.Core/Vault/OkfScope.cs` | 3 | `All` (39) |
| `src/Okf.Core/Search/OkfSearchQuery.cs` | 3 | `TokenizeWithOffsets` (32) |
| `src/Okf.Core/Bundle/OkfBundle.cs` | 3 | `Collect` (29) |
| `src/Okf.Cli/Upgrade/UpgradeCommand.cs` | 2 | `Run` (124) |
| `src/Okf.Cli/Mcp/McpCommand.cs` | 2 | `Run` (104) |
| `src/Okf.Core/Bundle/OkfDistribution.cs` | 2 | `Parse` (74) |
| `src/Okf.Cli/Bundle/BundleArguments.cs` | 2 | `Parse` (61) |
| `src/Okf.Core/Lint/OkfConfig.cs` | 2 | `ReadLint` (55) |
| `src/Okf.Core/Lint/LintText.cs` | 2 | `Resolve` (54) |
| `src/Okf.Core/Trust/OkfVerifyIdentity.cs` | 2 | `GlobalUserEmail` (52) |
| `src/Okf.Core/Documents/OkfDocument.cs` | 2 | `Parse` (50) |
| `src/Okf.Core/Upgrade/OkfUpgradeVersion.cs` | 2 | `ComparePrerelease` (38) |
| `src/Okf.Core/Capture/OkfCaptureManifest.cs` | 2 | `Parse` (34) |
| `src/Okf.Core/Vault/OkfDiscovery.cs` | 2 | `Discover` (34) |
| `src/Okf.Cli/Shared/CliApplication.cs` | 2 | `Run` (31) |
| `src/Okf.Cli/Shared/DiagnosticWriter.cs` | 2 | `ToJson` (27) |
| `src/Okf.Cli/Skills/SkillsArguments.cs` | 1 | `Parse` (125) |
| `src/Okf.Core/Lint/MarkdownScanner.cs` | 1 | `Scan` (83) |
| `src/Okf.Core/Site/OkfSiteMarkdown.cs` | 1 | `Render` (83) |
| `src/Okf.Cli/Upgrade/UpgradeArguments.cs` | 1 | `Parse` (82) |
| `src/Okf.Cli/Search/SearchArguments.cs` | 1 | `Parse` (79) |
| `src/Okf.Cli/Capture/CaptureArguments.cs` | 1 | `Parse` (76) |
| `src/Okf.Core/Upgrade/OkfUpgradeManifest.cs` | 1 | `Parse` (72) |
| `src/Okf.Cli/Site/SiteArguments.cs` | 1 | `Parse` (71) |
| `src/Okf.Cli/Init/InitArguments.cs` | 1 | `Parse` (63) |
| `src/Okf.Cli/Lint/LintArguments.cs` | 1 | `Parse` (60) |
| `src/Okf.Cli/Registry/RegistryArguments.cs` | 1 | `Parse` (59) |
| `src/Okf.Cli/Generated/GeneratedArguments.cs` | 1 | `Parse` (56) |
| `src/Okf.Cli/Inbox/InboxJson.cs` | 1 | `Write` (53) |
| `src/Okf.Cli/Inbox/InboxArguments.cs` | 1 | `Parse` (48) |
| `src/Okf.Cli/Index/IndexArguments.cs` | 1 | `Parse` (48) |
| `src/Okf.Cli/Verify/VerifyArguments.cs` | 1 | `Parse` (47) |
| `src/Okf.Cli/Search/SearchJson.cs` | 1 | `Write` (45) |
| `src/Okf.Core/Documents/FileLayout.cs` | 1 | `Of` (45) |
| `src/Okf.Core/Vault/OkfConcept.cs` | 1 | `Read` (44) |
| `src/Okf.Core/Documents/OkfLifecycleInstant.cs` | 1 | `TryParse` (33) |
| `src/Okf.Core/Documents/OkfActor.cs` | 1 | `IsValid` (30) |
| `src/Okf.Core/Documents/OkfValue.cs` | 1 | `IsZeroNumber` (30) |
| `src/Okf.Core/Lint/OkfDiagnostic.cs` | 1 | `CompareTo` (29) |
| `src/Okf.Cli/Shared/ScopeSettings.cs` | 1 | `Resolve` (28) |
| `src/Okf.Core/Vault/OkfEnvironment.cs` | 1 | `FromProcess` (23) |
| `src/Okf.Core/Lint/OkfSeverity.cs` | 1 | `TryParse` (22) |
| `src/Okf.Core/Lint/OkfSeverityResolver.cs` | 1 | `ResolveWithSource` (22) |

### What the ranking says

The weight is not evenly spread. Three shapes account for most of it:

1. **One method per rule, one file per rule set.** `OkfLinter` is the extreme
   case — 12 long methods, all of them `Check…` — and `OkfBundler`, `OkfIndex`
   and `OkfSearch` are the same shape. These are the highest-value files: each
   long method is already one concept, so the extraction is mechanical.
2. **Hand-rolled argument parsers.** Eleven `*Arguments.cs` files carry exactly
   one long method, always called `Parse`, always a `while` over `argv` with a
   `switch` inside it. AD-9 keeps `System.CommandLine` out, so these are ours to
   read; the loop is the algorithm and only the per-flag arms extract cleanly.
3. **Text emitters.** `CompletionCommand.Zsh` (154), `OkfSiteHtml.Landing` (87)
   and `RegistryCommand.WriteUsage` (84) are long because they are one string
   being built. Splitting them by paragraph moves lines around without making
   anything clearer; splitting them by *section of the output* does.

## Area: `Okf.Core/Documents/` + `Okf.Core/Lint/`

Lane `14-core-documents-lint`, branched off `dev` at `73c91ae`.

| File | Methods > 20, before → after | Longest method, before → after |
| --- | --- | --- |
| `Documents/FileLayout.cs` | 1 → 0 | `Of` (45) → `Of` (12) |
| `Documents/OkfActor.cs` | 1 → 0 | `IsValid` (30) → `IsValid` (18) |
| `Documents/OkfDocument.cs` | 2 → 1 | `Parse` (50) → `SplitLines` (21) |
| `Documents/OkfLifecycleInstant.cs` | 1 → 0 | `TryParse` (33) → `TryParse` (13) |
| `Documents/OkfValue.cs` | 1 → 0 | `IsZeroNumber` (30) → `IsZeroNumber` (15) |
| `Documents/YamlBridge.cs` | 3 → 1 | `EmitValue` (48) → `Load` (25) |
| `Lint/LintText.cs` | 2 → 1 | `Resolve` (54) → `ClassifyResource` (21) |
| `Lint/MarkdownScanner.cs` | 1 → 1 | `Scan` (83) → `Scan` (21) |
| `Lint/OkfConfig.cs` | 2 → 1 | `ReadLint` (55) → `ReadLint` (21) |
| `Lint/OkfDiagnostic.cs` | 1 → 1 | `CompareTo` (29) → `CompareTo` (29) |
| `Lint/OkfLinter.cs` | 12 → 0 | `CheckHygiene` (88) → `CheckDuplicateStem` (20) |
| `Lint/OkfSeverity.cs` | 1 → 1 | `TryParse` (22) → `TryParse` (22) |
| `Lint/OkfSeverityResolver.cs` | 1 → 1 | `ResolveWithSource` (22) → `ResolveWithSource` (22) |
| **Area** | **29 → 8** | |

### What made the split possible

`OkfLinter` was the file the ranking pointed at, and its twelve long methods
were not long because of the rules — they were long because every rule method
was carrying five to eight parameters through by hand. Two private nested
parameter objects removed that, and the rules then fell apart along the lines
the spec already draws:

- `BundleWalk` — the bundle, where its diagnostics go, and the four
  dictionaries a walk accumulates for the rules that need more than the file in
  front of them. Its one method, `Report(rule, message, path, line)`, replaced
  `diagnostics.Add(Diagnostic(rule, message, path, line, bundle))` at every call
  site.
- `LintedConcept` — path, layout, frontmatter and scan. `CheckConcept`'s eight
  parameters became two.

`CA1822` being a build error here turned out to be a useful second opinion:
almost every extracted method came back flagged "does not access instance data",
which is the analyzer noticing that the rules never used the linter's state in
the first place — only `CheckTags` (the tag registry), `CheckStaleness`
(today's date), `VaultDiagnostic` (a severity), and `CheckGeneratedIndexes` and
`CheckVault` (both read a severity before doing expensive work) touch `_options`
at all. Every other instance method in the file is one only because it sits on a
call path to one of those five.

### What extraction does to a mutation score

Worth writing down for the later lanes, because it is not obvious and it looks
like a regression when it is the opposite.

Stryker has a *block already covered* filter: within one method, once a mutant
in a block has been tested, mutants in blocks the same tests reach get skipped.
A 88-line method is one big block tree, so most of what is inside it is filtered
away. Split it into fifteen methods and the filter has far less to grip: the
same code produces **more tested mutants**, and every weakness the filter was
hiding surfaces at once.

Measured over this area with the scoped command below, before and after:

```sh
dotnet stryker --skip-version-check --reporter json \
  -m "**/Okf.Core/Documents/*.cs" -m "**/Okf.Core/Lint/*.cs"
```

Tested mutants went 774 → 853 (`OkfConfig` 42 → 64, `OkfLinter` 195 → 220,
`OkfDocument` 58 → 82). The 774 counts the ten the baseline left uncovered
alongside the 764 Stryker actually ran, so it is the same denominator as the
per-file figures beside it and as the 853. Killed rose in every touched file.
But on the first measurement four files' *scores* went **down**, purely because
their denominators grew: nine mutants the baseline never tested surfaced, and
survived. So a per-file percentage is not comparable across a composed-method
refactor unless the survivor sets are compared too — the number reads as a
regression when what happened is that the suite's existing holes became visible.

Six of the nine were real, untested behaviour, and this lane closed them with
tests: four `OkfConfig` rejection messages (blanking any of them cost nothing),
a `~~~` line inside a ```` ``` ```` block, and the two raw-immutability
diagnostics' instruction halves — the sentences that say *do not fix this by
editing the manifest*, which is the whole point of the rule. Two were an
artefact of the refactor's own shape and were removed rather than tested: a
`bool`-returning `Try…` helper puts a `return false;` in a `catch` no test
reaches, where the old inline `return SomeEnum.Value;` had nothing to mutate, so
those helpers return `string?` instead — the better shape anyway. One is
provably equivalent and cannot be killed: `if (end < 0)` in `OkfDocument.Parse`,
where `end` is `-1` or `≥ 1` and never `0`.

| File | Score, before → after |
| --- | --- |
| `Documents/FileLayout.cs` | 94.12 % → 94.23 % |
| `Documents/OkfActor.cs` | 100.00 % → 100.00 % |
| `Documents/OkfDocument.cs` | 89.66 % → 91.46 % |
| `Documents/OkfLifecycleInstant.cs` | 100.00 % → 100.00 % |
| `Documents/OkfValue.cs` | 87.91 % → 88.64 % |
| `Documents/YamlBridge.cs` | 78.43 % → 79.63 % |
| `Lint/LintText.cs` | 96.36 % → 95.00 % |
| `Lint/MarkdownScanner.cs` | 100.00 % → 100.00 % |
| `Lint/OkfConfig.cs` | 92.86 % → 100.00 % |
| `Lint/OkfLinter.cs` | 87.69 % → 88.64 % |
| **Area (scoped run)** | **90.96 % → 91.91 %** |

`LintText` is the one file still below where it started. Its *survived* count
went 2 → 1 (the refactor's `Resolve` split let a test kill the
`IndexOfAny(...) >= 0` mutant), and the two replacing it are block removals on
the `catch` clauses in `Unescaped` and `Combined`. Neither can be killed, and
the reason is Stryker's, not the suite's: emptying a `catch` body in a method
that must return a value would not compile, so Stryker supplies a
`return default;` — and `default(string?)` is exactly the `null` both clauses
return. The mutants are **provably equivalent**, whatever test reaches them.

The review checked the reachability claim behind that and found half of it
wrong, so it is written down properly here:

- `Uri.UnescapeDataString` really does not throw `UriFormatException` on
  .NET 10: 568 420 inputs — every string up to four characters over a
  percent-heavy alphabet, plus 400 000 random ones — produced none, and the
  Unix implementation has no throw path. That clause is unreachable.
- `Path.GetFullPath` *does* throw `ArgumentException`, on the empty string, and
  `Combined` reaches it. A bare `/` (or `%2F`) against an empty `bundleRoot`
  trims to nothing, `Path.Combine` returns `""`, and the invalid-character check
  never objects because `""` carries no invalid character. `OkfBundle.Root` is
  always `Path.GetFullPath`-ed and so is never empty, which is why no lint run
  can produce it — but `LintText.Resolve` is shared and internal, and
  `LintTextTests.ARootThatIsNotAPathResolvesToNothingRatherThanThrowing` now
  pins the no-throw answer. It moves the mutant from uncovered to covered and
  the score not at all, for the equivalence reason above.

Deleting either clause would be a behaviour change and is not this lane's to
make.

One caveat on the whole table. The review re-ran both sides of it
independently — the same scoped command, `dev` at `73c91ae` for the before
column — and reproduced seven of the ten *before* cells exactly (`FileLayout`
94.12 %, `OkfActor` and `OkfLifecycleInstant` and `MarkdownScanner` 100.00 %,
`YamlBridge` 78.43 %, `LintText` 96.36 %, `OkfConfig` 92.86 %, `OkfLinter`
87.69 %) and three not at all: `OkfDocument` came out at 91.38 % rather than
89.66 %, `OkfValue` at 91.21 % rather than 87.91 %, and the area at 91.47 %
rather than 90.96 %. Two reruns of the *after* tree gave 91.79 % and 92.03 %.

`OkfValue` and `OkfDocument` carry this area's only timeout-killed mutants, and
a timeout is a wall-clock judgement, so those two move with machine load — but
they are not the only ones that move: `FileLayout` read 94.23 % on one *after*
run and 96.15 % on the next, with nothing changed between them. Under the
review's numbers `OkfValue` *falls* across the refactor (91.21 % → 88.64 %)
rather than rising.

So read a per-file delta of a point or two as noise and compare survivor sets
instead. The deltas that are safe to read are the ones with a named cause:
`OkfConfig` +7.14 (four rejection messages now asserted), `MarkdownScanner` back
to 100.00 % (the foreign fence marker), and `OkfLinter` +0.95 on a denominator
that grew by 25.

### What was deliberately left long

Eight methods in the area are still over 20 lines. None of them is waiting for
somebody with more time:

- **`YamlBridge.Load` (25).** The scanner's blind spot hid this one — its
  interpolated `$"Invalid YAML in frontmatter: {ex.Message}"` is exactly the
  shape that unbalances the brace count — so it was neither extracted nor listed
  until the review found it. It is the AD-9 boundary call and its three failure
  modes: YamlDotNet threw, the stream held no document, and the stream held
  more than one. Each is a two-line guard over the same `stream`, and lifting
  them out would put the boundary in one method and what it refuses in three.
- **`OkfDiagnostic.CompareTo` (29).** A chain of tiebreaks — path, line, rule,
  message, then the raw ordinal fallback that keeps the order total. The chain
  *is* the algorithm; naming three of its five links would hide the one property
  a reader has to check, which is that every link falls through to the next.
- **`OkfSeverity.TryParse` (22).** A token table written as a `switch`. Nothing
  in it is a step.
- **`OkfSeverityResolver.ResolveWithSource` (22), `OkfConfig.ReadLint` (21),
  `MarkdownScanner.Scan` (21), `LintText.ClassifyResource` (21),
  `OkfDocument.SplitLines` (21).** Each is one line over after its extractions,
  and the only way to get under would be to delete a blank line or a comment.
  The 20 is a target, not a gate.

Nothing here needed a decisions.md entry: no AD moved, and every judgement call
above is about where a method boundary goes, not about what the code does.
