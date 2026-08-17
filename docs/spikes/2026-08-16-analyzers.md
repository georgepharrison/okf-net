# Analyzer triage: turning the Roslyn analyzers up to `latest-all`

Issue #31, step A0 of the polish plan. Dated 2026-08-16, run on `31-analyzers`
off `dev` at `c4e4a3e`. This is the fix-guide for the three follow-up work
packages: what fired, what was downgraded and why, and what is left per project.

Every number below came from one command, run twice — once before the config
change and once after:

```sh
dotnet build Okf.sln --no-incremental \
  -p:TreatWarningsAsErrors=false -p:CodeAnalysisTreatWarningsAsErrors=false
```

The two `-p:` flags are measurement scaffolding, not the shipped setting. Without
them `Okf.Core` fails first and the three projects downstream of it never
compile, so the histogram would only ever show one project's findings.
Diagnostics are counted once per (file, line, column, rule) — MSBuild prints each
one twice.

## What changed

`Directory.Build.props` gained the analysis gate, centralized so a fifth project
cannot join `Okf.sln` with a weaker setting than its four siblings:
`AnalysisLevel=latest-all`, `EnforceCodeStyleInBuild=true`,
`CodeAnalysisTreatWarningsAsErrors=true`, plus `Nullable` and
`TreatWarningsAsErrors`, which were four identical copies in four csprojs and are
now one. `PublishAot` and `IsAotCompatible` stayed where AD-8 put them.

`.editorconfig` at the repo root holds the style and naming rules and **every**
analyzer downgrade. `spikes/Directory.Build.props` keeps the throwaway out of it.

## Before and after

| | before | analyzers on, no downgrades | after downgrades |
| --- | --- | --- | --- |
| **total** | **0** | **149** | **56** |
| Okf.Core | 0 | 23 | 13 |
| Okf.Cli | 0 | 6 | 5 |
| Okf.Core.Tests | 0 | 90 | 10 |
| Okf.Cli.Tests | 0 | 30 | 28 |

The "before" column is zero because `TreatWarningsAsErrors` was already on: a
warning could not survive to be counted. The gate is not new; its reach is.

By rule:

| rule | before | on | after | |
| --- | --- | --- | --- | --- |
| CA1707 | 0 | 70 | 0 | underscores in test names — off in `tests/` only |
| CA2000 | 0 | 32 | 32 | dispose before losing the last reference |
| CA1859 | 0 | 15 | 13 | concrete type for a non-public member |
| CA1063 | 0 | 6 | 6 | implement `IDisposable` correctly |
| CA1054 | 0 | 5 | 0 | `Uri` instead of `string` — downgraded |
| CA1056 | 0 | 3 | 0 | ditto, on a property |
| CA1307 | 0 | 3 | 2 | `StringComparison` for clarity |
| CA1308 | 0 | 3 | 0 | `ToUpperInvariant` — downgraded |
| CA1062 | 0 | 3 | 0 | argument null check — downgraded |
| CA1822 | 0 | 3 | 1 | member can be static |
| CA1032 | 0 | 2 | 0 | exception ctor overloads — downgraded |
| CA1036 | 0 | 1 | 0 | comparison operators — downgraded |
| CA1815 | 0 | 1 | 0 | `Equals` on a struct — downgraded |
| CA1816 | 0 | 1 | 1 | `GC.SuppressFinalize` |
| CA1031 | 0 | 1 | 1 | catch of `Exception` |

No `CS`, `IL` or `SYSLIB` diagnostic fired in any run.

## AOT posture is unaffected

AD-8's claim is that a plain `dotnet build` runs the AOT and trim analyzers, so
an AOT-hostile dependency fails at the commit that added it. It still does — the
properties that decide it are untouched and were read back off the built projects
rather than assumed:

| | `Okf.Cli` | `Okf.Core` |
| --- | --- | --- |
| `PublishAot` | `true` | — |
| `IsAotCompatible` | — | `true` |
| `IsTrimmable` | — | `true` |
| `EnableAotAnalyzer` | `true` | `true` |
| `EnableTrimAnalyzer` | `true` | `true` |
| `EnableSingleFileAnalyzer` | `true` | — |

Zero `IL####` and zero `SYSLIB####` before, zero after. Whatever the three
follow-up packages do, an `IL` or `SYSLIB` diagnostic appearing in their build is
a new fact about AOT safety and not analyzer noise — it should stop the work.

## Rules downgraded, and why

Every one of these is a line in `.editorconfig` carrying the same reason. Nothing
was disabled that was not first measured firing: a rule that does not fire needs
no exemption, and listing it would only hide the day it starts.

Repo-wide:

| rule | severity | reason |
| --- | --- | --- |
| CA1054 | `none` | public API frozen 2026-08-15; and a `Uri`-typed manifest URL is re-escaped in transit, so the string the artifact host published stops round-tripping byte-for-byte |
| CA1056 | `none` | same, on properties: `OkfUpgradeAsset.Url`, `OkfUpgradeOptions.BaseUrl`, `OkfCaptureAddition.OriginalUrl` |
| CA1036 | `none` | public API frozen 2026-08-15; and defining `==` on `OkfDiagnostic` would silently retarget every existing reference-equality comparison of it to ordering-based equality |
| CA1815 | `none` | public API frozen 2026-08-15; and `OkfLifecycleInstant` compares at two precisions that deliberately disagree (date-as-written vs instant), so one `Equals` would have to pick a side and become a trap |
| CA1032 | `none` | the ctor overloads it asks for are dead API — `OkfUpgradeException` and `McpProtocolException` are only ever thrown by okf-net, always with a message |
| CA1062 | `none` | redundant under nullable reference types, which are on everywhere and already fail the build |
| CA1308 | `none` | **changes behaviour**: `OkfSeverity.TryParse` switches on lowercase spec tokens and `OkfUpgrade.Name` builds the lowercase asset name `okf-linux-x64`; `ToUpperInvariant` needs every literal inverted, and the asset name is wrong until it is |
| IDE0130 | `none` | namespace-per-folder is what vertical slicing decides, which is step G (#59), not a rename to do ahead of it |

In `[tests/**/*.cs]` only, on Ringo's call (2026-08-16) that analyzer style,
naming, globalization and design findings do not apply to test code: CA1707,
CA1303, CA1305, CA1307, CA1310, CA1311, CA1861, CA1859, CA1822, CA1515, CA2007,
CA1062, CA1054, CA1056, CA1034, CA1812. What deliberately stays fatal in tests is
what a failing analyzer actually predicts about a test — the xUnit analyzers,
because `xUnit1xxx`/`xUnit2xxx` catch the vacuous and wrong assertions AD-44
exists to prevent; the disposal and sync-over-async rules (CA2000, CA1816,
CA1849, CA2213, and CA1063 with them), because an undisposed listener or a
blocked thread is where a flaky test comes from; and every compiler diagnostic,
nullable included.

### Considered and left enabled, because they do not fire

Measured, not assumed. Each was on the candidate list and each produced zero
findings, so none of them got a suppression line: CA1303 (no localizable string
parameters reach `Console`), CA1848 and CA2254 (no `ILogger` in the repo), CA2007
and CA1849 (there is no `await` in the codebase at all), CA1812, CA1852, CA1002,
CA1819, CA1305, CA1310, CA1515, CA1416 (the `OperatingSystem.IsWindows()` guards
are recognized), IDE0058, IDE0045 and IDE0046.

The IDE style rules are all set as `suggestion` preferences, which a build
ignores. Two are promoted to `warning` because the tree already satisfies them
completely, so promoting ratchets instead of churning: file-scoped namespaces
(146 of 146 files) and braces on every block.

### The mechanism: `.editorconfig`, not `.globalconfig`

One mechanism, and it is `.editorconfig`. Two reasons, the second decisive:

1. Severities have to be scoped by path, and a global AnalyzerConfig has no glob
   sections — its only scoping is by absolute file path. `[tests/**/*.cs]` is not
   expressible there.
2. `AnalysisLevel=latest-all` is *itself* implemented as a per-rule global
   AnalyzerConfig the SDK generates. Between two configs naming the same rule the
   `.editorconfig` wins, so a downgrade written there takes effect and the same
   line in a `.globalconfig` is a coin toss nobody should read the SDK targets to
   call.

One thing that looks like it should work and does not: a `[spikes/**/*.cs]`
section exempting the whole directory. Naming every rule at once needs the bulk
key `dotnet_analyzer_diagnostic.severity`, and a **bulk key loses to a specific
one** — including the per-rule entries `latest-all` generates. Written as a
path-scoped section it parses cleanly and does nothing; verified by building the
spike with one in place and watching CA2100 come through it regardless. Hence
`spikes/Directory.Build.props`, which drops that project to `AnalysisLevel=latest`
and `CodeAnalysisTreatWarningsAsErrors=false` while keeping `Nullable` and the
compiler's own `TreatWarningsAsErrors`. `spikes/` is outside `Okf.sln` on purpose
(AD-43) and a throwaway that already answered its question is not worth a sweep.

## What is left: zero

All three work packages landed on `31-analyzers` (work item A1). `dotnet build
Okf.sln` is clean — 0 errors, 0 warnings — and `dotnet test Okf.sln` is green (876
Okf.Core.Tests, 449 passed + 1 skipped Okf.Cli.Tests). Every fix below is
behaviour-preserving; nothing in `Okf.Core`'s frozen public surface changed.

### Package 1 — `Okf.Core` (13, now 0)

| rule | site | what happened |
| --- | --- | --- |
| CA1307 | `OkfAgentPointer.cs:153` (`string.Replace`) | `StringComparison.Ordinal` added, as planned — a no-op that satisfies the rule |
| CA1307 | `OkfBundler.cs:463` (`string.IndexOf(char)`) | same |
| CA2000 | `OkfUpgrade.cs:572` (`new SocketsHttpHandler`) | suppressed at the site as planned (false positive; ownership transfers via `disposeHandler: true`) — **not** a `using`: wrapping the handler in `using var` would dispose it in the method's `finally` right after `HttpClient` is constructed but before the caller ever sends a request, breaking the client. The A1 task brief's shorthand ("real using") would have changed behaviour here; suppression is what "behaviour-preserving" requires |
| CA1859 | `OkfBundle.cs:145` `Walk` (return) | suppressed — return flows unchanged into the frozen public `MarkdownFiles()`/`ContentFiles()` |
| CA1859 | `OkfSkills.cs:67` `Load` (return) | suppressed — backs the frozen public `All` property via `Lazy<IReadOnlyList<OkfSkill>>` |
| CA1859 | `OkfSiteGenerator.cs:91` `MultiPage` (return) | suppressed — return flows unchanged into `OkfSitePlan.Files`, a frozen public constructor parameter/property |
| CA1859 | `OkfSiteBuilder.cs:416` `Crumbs` (return) | suppressed — return flows unchanged into `OkfSitePage.Crumbs`, a public property (internal setter) |
| CA1859 | `OkfInbox.cs:443` `DriftedSources` (return) | retyped to `List<OkfDriftedSource>` — private. The list it returns does reach public API, as `OkfInboxItem.DriftedSources`, but only as a *value*: the property is still declared `IReadOnlyList<OkfDriftedSource>` and the constructor parameter is unchanged, so no public signature moved and a `List<T>` is what was stored there before |
| CA1859 | `OkfAgentPointer.cs:265` `Splice` (`replacement` param) | retyped to `string[]` — private, sole caller passes an array; `.Count` uses in the body became `.Length` |
| CA1859 | `OkfScope.cs:261` `VaultOf` (`bundles` param) | retyped to `List<OkfBundle>` — private, both callers pass `List<OkfBundle>` |
| CA1859 | `OkfScope.cs:275` `Sentence` (`bundles` param) | retyped to `List<OkfBundle>`, same reasoning — this was the actual 10th CA1859 site (the triage's line number pointed here, not at `VaultOf`; `VaultOf` was fixed too, as a harmless consistent follow-on, though it was not itself one of the original 13) |
| CA1859 | `OkfUpgrade.cs:515` `Get` (return) | retyped `Stream` → the private nested `ResponseStream` — sole caller is a lambda converted to the public `Fetch` delegate (covariant return, no cast needed), so there is no public-signature reason to keep the base type |
| CA1859 | `OkfSiteBuilder.cs:313` `Resolve` (`pages` param) | retyped to `Dictionary<string, OkfSitePage>` — private, sole caller (`byId`) is always a `Dictionary` |
| CA1859 | `OkfSiteBuilder.cs:418` `Crumbs` (`pages` param) | retyped to `Dictionary<string, OkfSitePage>`, same reasoning — note this is the *parameter* on the same method whose *return type* was suppressed above; the two get different treatment because only the return value crosses into public API |

### Package 2 — `Okf.Cli` (5, now 0)

| rule | site | what happened |
| --- | --- | --- |
| CA1859 | `IndexCommand.cs:117` `WriteWrite` (`indexes` param) | retyped to `List<OkfIndex>` |
| CA1859 | `IndexCommand.cs:157` `WriteCheck` (`indexes` param) | retyped to `List<OkfIndex>` |
| CA1859 | `McpToolset.cs:773` `Strings` (return) | retyped to `List<string>` |
| CA1822 | `McpToolset.cs:253` `List` | made `static`, as planned; its one caller (`McpServer.cs`) had to change from `this.tools.List()` to `McpToolset.List()` — C# does not allow calling a static member through an instance reference (CS0176) |
| CA1031 | `McpServer.cs:170` | suppressed at the site, quoting the existing comment, as planned — catch left total, not narrowed |

`McpServer.cs:357` and the twelve other `catch (Exception …) when (…)` sites in
the repo are still filtered and never tripped CA1031.

### Package 3 — the two test projects (38, now 0)

| rule | where | what happened |
| --- | --- | --- |
| CA2000 | 28 `Okf.Cli.Tests` (`TestSupport.cs` ×4, `UpgradeCommandTests.cs` ×24) | `using`/`using var` added to every `StringWriter`, mechanical, per Ringo's call to keep the rule live rather than suppress it. Several sites needed restructuring — an inline `new StringWriter()` passed as a call argument became a named `using var output/error` declared just above the call, so both writers stay reachable for later `.ToString()` assertions |
| CA2000 | 3 `Okf.Core.Tests` | `OkfUpgradeHttpTests.cs:192` (`TcpListener`) and two `Reader(file.Content)` in `OkfSiteGeneratorTests.cs` (`:359`, `:429`) all got real `using`/`using var` |
| CA1063 | `Okf.Core.Tests` (6) | `OkfDiscoveryTests`, `OkfRegistryTests`, `OkfScopeTests` all sealed — one word each, confirmed to clear every finding |
| CA1816 | `OkfDiscoveryTests.cs:158` (1) | cleared by the same `sealed` |

No new rule appeared once these landed — `dotnet build Okf.sln` went straight to
0 errors after the above.

## Two flags for Ringo

Neither is this change's to decide, and both are recorded here so the config does
not quietly become the ruling.

**Private field naming.** `.editorconfig` says camelCase with no underscore,
because that is what all 44 private fields in the repo are today. Ringo's ruling
of 2026-08-15 was `_camelCase`, "non-negotiable"; the orchestrator's 2026-08-16
comment on #31 holds it at camelCase until he says otherwise. One line in
`.editorconfig` and one fixer run is the whole change when he calls it.

**`var`.** Set to `true:suggestion` — the preference matches the ~3700 `var`
sites in the tree, so IDE0007/IDE0008 stay quiet. Ringo's ruling is the opposite:
explicit types plus target-typed `new`, `var` only for anonymous types. That is a
tree-wide rewrite and the second half of #31, not this change. Flipping the three
`csharp_style_var_*` lines to `false:warning` is what starts it.

## Update, later the same day (#59)

`IDE0130` above is now `error`, not `none`: #59 did the vertical slicing this
document's downgrade was waiting on — `src/Okf.Core` and `src/Okf.Cli` are one
folder per capability, the namespace follows the folder, and `.editorconfig`
enforces it on every build from here on. Full mapping and reasoning in
`docs/decisions.md`'s #59 section.

One rule joined the downgrade list that this triage did not anticipate: `CA1716`
started firing on `Okf.Cli.Tests.Shared` (test classes there must be `public` for
xUnit discovery, and `Shared` collides with VB.NET's `Shared` keyword). Neither
`Okf.Cli` nor `Okf.Cli.Tests` is ever packed or consumed outside this solution,
so it is `none` too — see the `.editorconfig` line for the exact reasoning.
`dotnet build Okf.sln` is still 0 errors, 0 warnings after both changes.
