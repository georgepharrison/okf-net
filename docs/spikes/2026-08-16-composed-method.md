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

It also has a known blind spot: it silently skips some members, so the count
column is a **floor**, not a total, and a file's real longest method may be one
the scanner never saw. Read the ranking as "at least this much work here". Seven
rows below were re-measured by hand against `dev` at `73c91ae` and corrected.

The first lane read that blind spot as brace-bearing string literals —
interpolated holes and embedded JSON — unbalancing the brace depth. **That
diagnosis is wrong**; see "A correction to the scanner" below. The cause is
nullable return types, and the fix is one line.

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

## Area: `Okf.Core/Index/` + `Okf.Core/Search/` + `Okf.Core/Trust/`

Lane `14-core-index-search-trust`, branched off `dev` at `0c97802`.

| File | Methods > 20, before → after | Longest method, before → after |
| --- | --- | --- |
| `Index/OkfIndex.cs` | 6 → 4 | `Plan` (143) → `Flatten` (27) |
| `Search/OkfSearch.cs` | 5 → 2 | `Search` (78) → `ReadCorpus` (27) |
| `Search/OkfSearchQuery.cs` | 3 → 2 | `TokenizeWithOffsets` (32) → `TokenizeWithOffsets` (32) |
| `Trust/OkfInbox.cs` | 4 → 3 | `Classify` (46) → `ReadConcepts` (26) |
| `Trust/OkfStamp.cs` | 5 → 1 | `TryInsert` (99) → `VerifyText` (23) |
| `Trust/OkfTrustTier.cs` | 0 → 0 | — |
| `Trust/OkfVerifyIdentity.cs` | 2 → 1 | `GlobalUserEmail` (52) → `FromConfig` (22) |
| **Area** | **25 → 13** | |

The area went from 59 methods to 97, and the longest method left in it is one
the lane did not touch.

### A correction to the scanner

The blind spot the first lane found is not string literals. It is **nullable
return types**: the scanner rejects a signature whose preceding text ends in
`?`, because that is how a ternary's `?` looks, and
`private static string? TryInsert(…)` looks the same. Every method it silently
skipped in this area — `TryInsert`, `TryStampGenerated`, `Classify`, four of
`OkfIndex`'s readers — returns a nullable. Allowing a trailing `?` when a type
character precedes it makes the scanner reproduce the seven hand-corrected rows
above exactly, so the counts in this section are machine-measured rather than
hand-checked. The `over20` column of the solution-wide inventory is still a
floor for the areas nobody has re-measured.

### What made these splits possible

- **`OkfIndexGenerator.Plan` (143 → 21).** Three things shared one scope:
  walking the bundle's files into three dictionaries, deciding what each
  directory's index would list, and turning that into indexes with drift
  statuses. The walk became a private `BundleTree` — every directory an index
  could belong in, the concepts under each, and the `index.md` already on disk
  there — and the other two became `EntriesByDirectory` and `PlanDirectory`.
  AD-13's one renderer is untouched; AD-14/15's four statuses moved intact into
  a `Status(existing, content)` switch, which is now the only place a status is
  decided.
- **`OkfStamp.TryInsert` (99) and `TryStampGenerated` (56) → 16 and 18.** Half
  of each was the same code: split the file into lines, require an opening
  fence, find the closing one, note which ending the fence carries, locate a
  top-level key, and work out which lines its value spills onto. That is one
  concept — the frontmatter block seen as the file's own lines — and it is now
  `FrontmatterText`, with a `FrontmatterKey` record for where one key's value
  sits. What is left in each method is exactly the shapes §5.2 permits:
  `TryInsert` names its four (`OpenListWithFirstEvent`,
  `NormalizeBareMappingIntoList`, `AppendEventUnderEmptyKey`,
  `AppendEventToSequence`) and `TryStampGenerated` its two. The parse-back check
  did not move: `Accepts` and `AcceptsGenerated` are still the last thing
  `VerifyText` and `StampGeneratedText` do before returning, on the insertion
  path and on the emitter path alike.
- **`OkfSearchEngine.Search` (78 → 24).** Read the corpus, rank it, report it.
  `ReadCorpus` and `Rank` are the first two, and the corpus rule reads better
  for the move: `Rank` takes the whole corpus and computes
  `Statistics.Of(corpus, terms)` before it filters, so "statistics over the
  corpus, not over the candidates" is visible in one method rather than spread
  over forty lines. The total order is the tail of `Rank` and unchanged, as are
  the BM25 constants.
- **`OkfInboxScanner.Classify` (46 → 18).** `IsUnacknowledged` was carrying six
  parameters, which is what a missing type looks like. A private `Lifecycle`
  record — the `generated` block, both timestamps as written and as parsed, the
  latest verification, `status` and `stale_after` — took them, and
  `IsUnacknowledged(concept, lifecycle)` now has two. The rule that the tier
  comes from `verified` alone is untouched; the record carries no tier.

`Scan` and `ReadCorpus` ended up the same shape as each other, which they were
not before: both return `(everything parsed, how many were skipped)` and leave
the judging to the caller. That cost `okf inbox` its streaming read — it holds
every `OkfConcept` for the length of a scan now, as search has always held its
corpus — and bought a `Scan` of eleven lines.

### The `Try…` helper trap, again

The first lane found that a `bool`-returning `Try…` helper puts an unkillable
`return false;` in a `catch` and switched those helpers to `string?`. That is
only half the rule. A helper returning `OkfDocument?` has the same problem for
the same reason: Stryker's block-removal mutant empties the `catch` body and
supplies `return default;`, and `default(OkfDocument?)` **is** the `null` the
clause returns. Both `OkfSearchEngine` and `OkfInboxScanner` grew such a helper
in this lane and both were reverted to an inline `try`/`catch` whose clause does
the counting (`skipped++`) rather than the returning. Emptying *that* clause
loses the count, which a test notices.

The shape to avoid is not "a `Try…` helper" — it is **a `catch` clause whose
only statement returns a value equal to `default`**. `TargetEnd` in
`OkfSearchEngine` hit the same trap without a `catch`, in an
`if (…) { return null; }` guard whose fall-through also returns null; it is
written as a `switch` expression with no block instead.

The inline form has a second virtue the helper form loses, and it is the reason
both `try` blocks here guard the parse *only* and not the indexing that follows
it. `Concept.Of` and `OkfConcept`'s constructor read a document that already
parsed; neither can raise `OkfDocumentException` today. If one ever did, a
`try` wrapped around both would quietly count the bug as an unreadable file.
Keeping the block tight costs each method five lines and is why `ReadCorpus`
and `ReadConcepts` are on the list of methods left over 20.

### Mutation: killed rose everywhere, and one file's percentage did not

Measured before and after with the same scoped command:

```sh
dotnet stryker --skip-version-check --reporter json \
  -m "**/Okf.Core/Index/*.cs" -m "**/Okf.Core/Search/*.cs" \
  -m "**/Okf.Core/Trust/*.cs"
```

| File | Killed, before → after | Score, before → after |
| --- | --- | --- |
| `Index/OkfIndex.cs` | 78 → 106 | 90.70 % → 90.60 % |
| `Search/OkfSearch.cs` | 281 → 298 | 91.23 % → 91.41 % |
| `Search/OkfSearchQuery.cs` | 52 → 53 | 100.00 % → 100.00 % |
| `Trust/OkfInbox.cs` | 45 → 51 | 100.00 % → 100.00 % |
| `Trust/OkfStamp.cs` | 132 → 91 | 81.99 % → 83.49 % |
| `Trust/OkfTrustTier.cs` | 3 → 3 | 100.00 % → 100.00 % |
| `Trust/OkfVerifyIdentity.cs` | 34 → 37 | 73.91 % → 75.51 % |
| **Area (scoped run)** | **625 → 639** | **89.16 % → 90.25 %** |

`OkfStamp`'s killed count falls because the file lost half its text surgery to
deduplication: its tested mutants went 161 → 109 and its **survivors went 20 to
10**, which is the number that says what happened. Six of the ten it shed were
one mutant reported twice — the same guard, the same fence scan, the same region
walk, once in `TryInsert` and once in `TryStampGenerated`.

`OkfIndex` is the one file whose percentage did not rise, by a tenth of a point,
on a denominator that grew 86 → 117. Its survivor set is the honest account:

- **Gone:** the `start < 0` check in the old `IsGenerated` (`AfterFrontmatter`
  returns `int?` now, so there is nothing to compare), and `OkfIndex.ToString`,
  which had no test at all and now has one.
- **Carried over, all five unkillable:** three at `Line(lines, index)`, whose
  `index < lines.Length` false branch no caller can reach, and the two loop
  bounds that guard it.
- **New, all in `BundleTree`, all equivalent:** the `{ root }` initializer, the
  `ThenBy` in `DeepestFirst`, and four in the ancestor walk — the `>=` length
  guard, its `&&`, the `||` on the break condition, and the `break` itself. (Six
  on the review's run, five on this lane's; which of them the coverage filter
  selects moves between runs, and both counts describe the same set of
  locations.) They are equivalent for one reason: the walk stops at the bundle
  root because `_directories.Add(root)` returns `false` there, so the length
  guard, the `break` and the initializer each cover what the others do, and
  every ancestor of a directory already in the set is itself already in it.
  Four were checked rather than argued — applied to the source, the suite stays
  green and every `okf index --check --json` surface in the repo is
  byte-identical; the `&&`-to-`||` one does not compile outside Stryker's
  sandbox, and the `ThenBy` one only reorders same-length sibling directories,
  which are independent of each other and re-sorted afterwards anyway.

None of the five was visible before, because the old 143-line `Plan` was one
block tree and Stryker's *block already covered* filter hid them. That is the
first lane's finding restated: extraction does not weaken a suite, it stops the
filter hiding what the suite never covered.

Read a per-file delta of a point or two as noise here too. Three runs of the
same scoped command over this lane's tree returned 90.60 %, 88.89 % and 90.60 %
for `OkfIndex` as tests were added; which mutants the coverage filter selects
moves between runs.

Six tests were added, each shown to fail against the mutant it is there for:

- `okf index` honours a supplied file list, and honours a supplied text cache.
  The old cache test asserted `Assert.All` over a list that was always empty —
  vacuous, and it passed with the caches ignored.
- a plan is ordered by bundle-relative path (PRD ACC-7); an orphan renders to
  nothing; an index prints as its path and status.
- `okf inbox` judges staleness against the injected date and not the clock
  (PRD CORE-7).
- `okf search` searches supplied text rather than the file on disk — the warm
  cache seam had no test at all.

### What was left long in index, search and trust

Thirteen methods in the area are still over 20 lines.

- **`OkfTokenizer.TokenizeWithOffsets` (32) and `Tokenize` (26)** — untouched,
  and the one judgement call here worth arguing with. They are two copies of one
  rule (lowercase, split on everything that is not a letter or a digit, by
  `Rune`), and the obvious dedup is to have `Tokenize` project
  `TokenizeWithOffsets`. It was not taken: `Tokenize` is the corpus-indexing hot
  path — called for `title`, `type`, `description`, every tag and the whole body
  of every concept — and routing it through the offset tokenizer would allocate
  a three-tuple per token in order to discard two thirds of it. Neither method
  is long for want of structure; each is one rune loop, and the loop is the
  algorithm. The duplication is real and is written down so the next reader does
  not have to rediscover that it was seen.
- **`OkfIndexGenerator.Flatten` (27)** — one whitespace-collapsing loop.
  `OkfSearchEngine.CollapseWhitespace`, extracted in this lane, is the same loop
  in another file; joining them is code motion across types, which is #15's job.
- **`OkfIndexGenerator.Compare` (23)** — the tiebreak chain: subdirectory last,
  then section case-insensitively, then ordinally, then link the same way. The
  chain is the algorithm, and naming three of its five links would hide the
  property a reader has to check, which is that every link falls through to the
  next. `OkfDiagnostic.CompareTo` was left long for this reason in the first
  lane.
- **`OkfSearchEngine.Search` (24)** — ten of its lines are the
  `OkfSearchOutcome` object initializer, which is the method's return shape.
  Hiding it behind a seven-argument constructor call trades a readable literal
  for an unreadable one.
- **`OkfStamp.VerifyText` (23)** — three of its lines are the paragraph
  explaining why the emitter fallback is held to the same parse-back terms as
  the insertion. The check has to stay in the public method, and the paragraph
  has to stay with the check.
- **`OkfSearchEngine.ReadCorpus` (27) and `OkfInboxScanner.ReadConcepts` (26)**
  — two nested loops and a `try`/`catch` whose clause counts rather than
  returns, for the reason above. Lifting the inner loop out would need a helper
  that either returns the unkillable `default` or takes the counter by
  reference; neither is clearer than the loop.
- **`Plan` (21), `Render` (21), `LatestVerification` (21),
  `DriftedSources` (22), `FromConfig` (22)** — each is one or two lines over
  after its extractions, and the only way under would be to delete a blank line
  or a comment. `FromConfig` is the clearest: three refusals §7 spells out, one
  `throw` each.

No AD moved and no decisions.md entry was needed: every judgement call above is
about where a method boundary goes, not about what the code does.

## Area: vault, capture, bundle, site, skills and upgrade

Lane `14-core-vault-bundle-site`, branched off `dev` at `7b09a9a`. The corrected
Python scanner used by the earlier lanes reports **77 → 3 methods over 20
lines**. The skills files were confirmed clean and left untouched.

| File | Methods > 20, before → after | Longest method, before → after |
| --- | --- | --- |
| `Vault/OkfAgentPointer.cs` | 3 → 0 | `WriteAgentsMd` (41) → `FindFences` (20) |
| `Vault/OkfConcept.cs` | 2 → 0 | `Read` (44) → `OkfConcept` (18) |
| `Vault/OkfDiscovery.cs` | 2 → 0 | `Discover` (34) → `WalkedUpVault` (16) |
| `Vault/OkfEnvironment.cs` | 1 → 0 | `FromProcess` (23) → `CapturedVariables` (11) |
| `Vault/OkfRegistry.cs` | 6 → 0 | `Parse` (54) → `UniqueId` (16) |
| `Vault/OkfScaffold.cs` | 4 → 0 | `Initialize` (40) → `LinkTarget` (18) |
| `Vault/OkfScope.cs` | 3 → 0 | `All` (39) → `RealPath` (17) |
| `Capture/OkfCaptureManifest.cs` | 2 → 0 | `Parse` (34) → `Parse` (17) |
| `Capture/OkfCaptureWriter.cs` | 9 → 0 | `Describe` (73) → `Close` (19) |
| `Bundle/OkfBundle.cs` | 3 → 0 | `Collect` (29) → `TryAdvanceSegment` (15) |
| `Bundle/OkfBundler.cs` | 8 → 0 | `Verify` (92) → `Write` (19) |
| `Bundle/OkfDistribution.cs` | 2 → 0 | `Parse` (74) → `Parse` (16) |
| `Site/OkfSiteAssets.cs` | 0 → 0 | `Read` (8) → `Read` (8) |
| `Site/OkfSiteBuilder.cs` | 8 → 0 | `Build` (125) → `Build` (19) |
| `Site/OkfSiteGenerator.cs` | 3 → 0 | `MultiPage` (65) → `ConceptFile` (17) |
| `Site/OkfSiteHtml.cs` | 9 → 0 | `Landing` (87) → `SiteData` (19) |
| `Site/OkfSiteMarkdown.cs` | 2 → 0 | `Render` (83) → `RewriteLink` (15) |
| `Site/OkfSiteModel.cs` | 0 → 0 | `OkfSiteModel` (14) → `OkfSiteModel` (14) |
| `Skills/OkfSkillInstaller.cs` | 0 → 0 | `Write` (20) → `Write` (20) |
| `Skills/OkfSkills.cs` | 0 → 0 | `Load` (19) → `Load` (19) |
| `Upgrade/OkfUpgrade.cs` | 7 → 2 | `Apply` (75) → `Apply` (22) |
| `Upgrade/OkfUpgradeManifest.cs` | 1 → 1 | `Parse` (72) → `Asset` (22) |
| `Upgrade/OkfUpgradePlan.cs` | 0 → 0 | `OkfUpgradePlan` (10) → `OkfUpgradePlan` (10) |
| `Upgrade/OkfUpgradeVersion.cs` | 2 → 0 | `ComparePrerelease` (38) → `ComparePrereleasePresence` (19) |
| **Area** | **77 → 3** | |

### What made the H3 splits possible

- The vault and registry methods each mixed resolution, validation and projection.
  Naming those phases made `Discover`, `Parse`, `All` and `Initialize` read in the
  same order the command performs them. Small private records such as
  `ResolvedConceptPath`, `BundleAccumulator` and `AgentFileLayout` kept the steps
  below four parameters without exposing a new API.
- Capture's byte-offset surgery already had real domain objects hidden as parallel
  locals. `AdditionContext`, `ClosureContext`, `LocatedManifest` and
  `LocatedEntry` made append, replace, close and parse-back verification separate
  named operations while preserving every byte outside the splice.
- Bundle planning, writing and verification were three concepts in one scope.
  `DistributionContents`, `VerificationInput` and `VerificationPreparation` let
  the public methods state those phases directly. The archive loops stayed on the
  type that already owned them; only their format-specific steps moved out.
- Site rendering split by output section. Landing, dashboard, graph, article facts,
  sources and backlinks now each own the markup they emit. A private `PageRequest`
  replaced a seven-parameter page method, and separate with-data/without-data paths
  removed the boolean behaviour flag without changing the generated bytes.
- Upgrade now says resolve release, select asset, stage, verify and swap. Redirects
  have separate HTTPS-preserving and loopback-HTTP paths; the frozen public
  `ResolveRedirect(..., bool)` signature remains, but no new private behaviour flag
  was introduced.

The behaviour check built `origin/dev` separately and compared stdout, stderr,
exit code and generated file hashes for help, lint, search JSON, index JSON, inbox
JSON, site JSON and every generated page, bundle help, a fixed-stamp directory
bundle and verify, skills list, and upgrade help. It caught one extraction error:
`generatedAt` moved ahead of `sourceVault` in `okf-bundle.json`. Restoring the key
order made every compared byte identical.

### Mutation: more isolated blocks exposed more of the existing suite

The exact scoped command from the lane brief was run after the refactor. Four real
holes were then closed and the command was repeated so this table describes the
committed tests. Tested mutants grew **2,265 → 2,598**; killed mutants grew
**1,909 → 2,134**, while survivors grew **286 → 377** and uncovered mutants
**57 → 74**. The area score is **84.86% → 82.64%**: the denominator grew by 333
because extraction stopped Stryker's block-already-covered filter hiding isolated
branches.

| File | Killed, before → after | Survived, before → after | Score, before → after |
| --- | --- | --- | --- |
| `Bundle/OkfBundle.cs` | 51 → 54 | 4 → 8 | 89.47% → 83.08% |
| `Bundle/OkfBundler.cs` | 118 → 145 | 15 → 24 | 88.06% → 85.29% |
| `Bundle/OkfDistribution.cs` | 70 → 81 | 1 → 3 | 98.59% → 96.43% |
| `Capture/OkfCaptureManifest.cs` | 17 → 32 | 0 → 2 | 94.44% → 84.21% |
| `Capture/OkfCaptureWriter.cs` | 206 → 228 | 36 → 52 | 82.61% → 79.38% |
| `Site/OkfSiteAssets.cs` | 0 → 0 | 0 → 0 | 0.00% → 0.00% |
| `Site/OkfSiteBuilder.cs` | 140 → 181 | 16 → 24 | 90.06% → 88.57% |
| `Site/OkfSiteGenerator.cs` | 53 → 44 | 32 → 30 | 62.35% → 59.46% |
| `Site/OkfSiteHtml.cs` | 548 → 551 | 79 → 80 | 87.12% → 87.18% |
| `Site/OkfSiteMarkdown.cs` | 30 → 52 | 3 → 13 | 86.11% → 77.94% |
| `Site/OkfSiteModel.cs` | 10 → 9 | 1 → 2 | 90.91% → 81.82% |
| `Skills/OkfSkillInstaller.cs` | 33 → 33 | 1 → 1 | 97.06% → 97.06% |
| `Skills/OkfSkills.cs` | 8 → 4 | 2 → 6 | 66.67% → 33.33% |
| `Upgrade/OkfUpgrade.cs` | 131 → 150 | 31 → 44 | 76.44% → 70.37% |
| `Upgrade/OkfUpgradeManifest.cs` | 4 → 34 | 1 → 7 | 80.00% → 80.95% |
| `Upgrade/OkfUpgradePlan.cs` | 2 → 2 | 2 → 2 | 50.00% → 50.00% |
| `Upgrade/OkfUpgradeVersion.cs` | 56 → 61 | 5 → 5 | 90.32% → 91.04% |
| `Vault/OkfAgentPointer.cs` | 74 → 78 | 1 → 1 | 98.67% → 98.73% |
| `Vault/OkfConcept.cs` | 35 → 43 | 8 → 9 | 77.78% → 81.13% |
| `Vault/OkfDiscovery.cs` | 35 → 38 | 4 → 5 | 85.37% → 84.44% |
| `Vault/OkfEnvironment.cs` | 14 → 14 | 1 → 9 | 53.85% → 51.85% |
| `Vault/OkfRegistry.cs` | 95 → 120 | 13 → 14 | 84.82% → 82.19% |
| `Vault/OkfScaffold.cs` | 123 → 118 | 25 → 29 | 80.13% → 77.42% |
| `Vault/OkfScope.cs` | 56 → 62 | 5 → 7 | 90.32% → 84.93% |
| **Area (scoped run)** | **1,909 → 2,134** | **286 → 377** | **84.86% → 82.64%** |

The survivor-set review found four contracts worth adding tests for: an external
site link keeps `target="_blank"`; a blocked destination stays visibly `broken`;
a registry required field with the wrong JSON type is rejected; and mistyped
optional distribution arrays are treated as absent. Each test was shown to fail
against the exact source mutation before the source was restored. The remaining
newly visible set is dominated by emitter string mutations, private JSON-walk
mechanics, platform-only environment branches and equivalent initializer/default
mutants rather than a changed public contract.

The run also demonstrates the noise warning from H1/H2 without inference:
`OkfSkills.cs` and `OkfSiteModel.cs` were not touched, yet their scores moved from
66.67% to 33.33% and 90.91% to 81.82% respectively. Percentages and even which
mutant the coverage filter selects are not stable enough to overrule the survivor
classification.

### What H3 deliberately left long

Three methods remain over 20 lines:

- **`OkfUpgrade.Apply` (22).** The public install boundary is one `try` with the
  exception translation and the `finally` that guarantees staging cleanup.
  Extracting either clause would separate the operation from its atomicity rule.
- **`OkfUpgrade.Get` (22).** The bounded redirect loop is the algorithm: issue a
  request, preserve the initial scheme policy on every redirect, return the first
  successful response, otherwise stop at the hop limit. Further extraction either
  reintroduces a boolean behaviour flag or hides the invariant in mutable state.
- **`OkfUpgradeManifest.Asset` (22).** One strict-JSON projection validates the
  required path and digest before constructing an asset. It is two lines over, and
  moving the refusal away from the projection makes the security boundary harder to
  inspect.

No AD moved and no decisions.md entry was needed: the three calls above concern
method boundaries, not what the code does.
