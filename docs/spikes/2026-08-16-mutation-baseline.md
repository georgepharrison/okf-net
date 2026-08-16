# Mutation baseline and triage, 2026-08-16

Issue #13, step C of the polish plan. Dated 2026-08-16, run on
`13-mutation-baseline` off `dev` at `0fa6bf5`. One command, no exclusions:

```sh
mise run mutate
```

This is the input the D and E work packages start from: what the suite does not
constrain, split into *no coverage* (no test executes the code at all) and
*survived* (a test executes it and does not notice it changed).

The raw reports are ~16 MB each and are deliberately not committed —
`artifacts/stryker/` is gitignored and the CI `mutation` job keeps the HTML,
JSON and markdown for 30 days. Only the numbers and the judgement live here.

## Headline

**73.23 %**, 21 m 47 s on 8 cores, 10 986 mutants created.

| Project  | Score   | Killed | Survived | No coverage | Timeout | Ignored | Compile error | Total  |
| -------- | ------- | ------ | -------- | ----------- | ------- | ------- | ------------- | ------ |
| Okf.Core | 70.11 % | 2711   | 995      | 170         | 21      | 745     | 1888          | 6530   |
| Okf.Cli  | 76.87 % | 2579   | 553      | 223         | 0       | 350     | 751           | 4456   |
| Solution | 73.23 % | 5290   | 1548     | 393         | 21      | 1095    | 2639          | 10 986 |

Score is `(killed + timeout) / (killed + timeout + survived + no coverage)`;
ignored and compile-error mutants are not scored.

**Against the previous baseline** (work item #11, tip of `main` at `1f334c9`,
3413 mutants, 71.29 % after its six new tests):

| | 2026-08-15 | 2026-08-16 | Δ |
| --- | --- | --- | --- |
| Mutants created | 3413 | 10 986 | ×3.2 |
| Solution score | 71.29 % | 73.23 % | +1.94 |
| Okf.Core | 71.19 % | 70.11 % | −1.08 |
| Okf.Cli | 71.44 % | 76.87 % | +5.43 |
| Survived | 493 | 1548 | ×3.1 |
| No coverage | 136 | 393 | ×2.9 |
| Runtime | 6 m 58 s | 21 m 47 s | ×3.1 |

The code roughly tripled (75 files, +17 296 lines across `okf upgrade`, the
registry, completions, the site, capture/generated and the agent pointer) and
the score went **up** ~2 points. The two projects no longer land within a point
of each other: `Okf.Cli` gained 5.4 points, almost entirely from
`CompletionCommand` (95.88 %) and `CompletionTable` (94.20 %), which are golden-
file tested and contribute 943 mutants between them. `Okf.Core` slipped a point
because the site renderer arrived with 2102 mutants at 59.54 %.

The previous baseline's conclusion — no weak half to attack — no longer holds.
There is now a weak *quarter*, and it is the site.

## Thresholds

`stryker-config.json` today: `break` 60, `low` 70, `high` 85.

| Threshold | Set | Achieved | Verdict |
| --- | --- | --- | --- |
| `break` (exit non-zero below) | 60 | 73.23 | 13.2 points of headroom |
| `low` (report turns red) | 70 | 73.23 | 3.2 points of headroom; still the right tripwire |
| `high` (target) | 85 | 73.23 | 11.8 points short |

All three survive the tripling intact and none needs to move today. `low: 70`
did its job: `Okf.Core` at 70.11 % is 0.11 points above it, which is the signal
the threshold exists to give.

**Recommendation for `break` once E is done.** Do not raise it in this MR —
`break` is the only number with teeth, and raising it before the tests exist
turns a scheduled run red for a reason nobody has acted on yet. After E lands,
set **`break: 70`** and leave `low` and `high` where they are. The reasoning
matches the original: `break` sits ~11 points under the achieved score so
ordinary churn cannot flake it. If E lands the package estimates below, the
achieved score should be 80–82 %, which puts a `break` of 70 at the same
comfortable distance 60 had from 70.79 % a day ago. A `break` above 75 is not
defensible while 39.8 % of survivors are string literals nobody intends to pin.

## What the survivors actually are

Classifying all 1548 survivors by the source line they sit on:

| Class | Count | Share |
| --- | --- | --- |
| String literal (diagnostic prose, format strings) | 616 | 39.8 % |
| Equality mutation | 200 | 12.9 % |
| `ArgumentNullException.ThrowIfNull` / `ThrowIfNullOrEmpty` guards | 177 | 11.4 % |
| Statement removal (non-guard) | 162 | 10.5 % |
| Logical / conditional / boolean | 229 | 14.8 % |
| Everything else (arithmetic, LINQ, null-coalescing, block removal, …) | 164 | 10.6 % |

Two of these classes are **equivalent by construction and should never be
chased**, and together they are just over half the survivor list:

- **String-literal mutants on diagnostic prose (616).** Unchanged from the
  previous baseline's reasoning: where a message is a contract it is already
  pinned; where it is wording, pinning it verbatim buys a change-detector test,
  which is the vacuity AD-44 exists against.
- **Null-guard mutants (177) — new since the last baseline, and worth naming.**
  These did not exist on 2026-08-15. They arrived with the analyzer pass (work
  item #31), which turned `AnalysisLevel` up to `latest-all` and made CA1062
  an error, so every public entry point gained
  `ArgumentNullException.ThrowIfNull(x)`. Every one of those guards is
  unkillable: the callers are all in-repo and none of them passes null, so
  deleting the guard changes no observable behaviour. **This is a mechanical
  interaction between two quality gates** — one gate adds code the other gate
  then reports as untested — and it costs 2.45 points of raw score (73.23 % →
  75.68 % if every guard mutant were killed) for no
  defect risk. Do not write null-argument tests to kill them; that is a test
  suite documenting the analyzer rather than the product.

So of 1548 survivors, **755 (48.8 %) are actionable at all**, and the package
table below counts only those.

## Work packages

Counts are (survivors, of which actionable, no-coverage mutants). Effort is for
the *actionable* survivors plus the reachable no-coverage sites.

| Package | Score | Survived | Actionable | No cov. | Effort |
| --- | --- | --- | --- | --- | --- |
| bundle+site | 59.54 % | 439 | 174 | 34 | **L** |
| CLI adapters | 76.87 % | 553 | 249 | 223 | **L** |
| vault/skills/registry/upgrade/capture | 73.03 % | 235 | 110 | 63 | **M** |
| index+search | 70.37 % | 121 | 106 | 15 | **M** |
| parse+lint | 78.35 % | 139 | 79 | 42 | **S** |
| trust/inbox/verify/stamp | 76.52 % | 61 | 37 | 16 | **S** |

- **bundle+site — L, and the one package that moves the solution score.**
  2102 mutants at 59.54 %, more than a fifth of everything scored, and the only
  package below `break`. `OkfSiteHtml.cs` alone is 1118 mutants at 55.17 %, but
  162 of its 265 survivors are HTML string fragments and only 103 are
  actionable — the rest of the work is `OkfSiteBuilder.cs` (60.61 %, 39) and
  `OkfSiteGenerator.cs` (45.56 %). L because the fixture cost is real: the site
  needs a multi-bundle vault rendered and asserted structurally, not a
  golden-file diff of every fragment.
- **CLI adapters — L by volume, S by difficulty.** 223 no-coverage mutants,
  the largest block, and they are almost all `--verbose` diagnostics and
  argument-parser error branches that no test invokes. Each is cheap
  individually (one `Run` call asserting one stderr line); there are just a lot
  of them. The argument parsers are the highest value: `RegistryArguments.cs`
  (44.83 %), `IndexArguments.cs` (51.11 %), `SiteArguments.cs` (56.92 %),
  `SearchArguments.cs` (57.81 %), `CaptureArguments.cs` (58.33 %).
- **vault/skills/registry/upgrade/capture — M.** `OkfUpgrade.cs` (70.49 %) and
  `OkfRegistry.cs` (66.93 %) carry it. Several are platform-gated (Windows
  branches unreachable on the test host) and honestly unkillable here.
- **index+search — M, and the best actionable density in the solution.** 121
  survivors, 106 of them actionable (87.6 %) — almost no prose to discount.
  `OkfSearch.cs` (71.70 %, 79 actionable) and `OkfIndex.cs` (58.95 %, 19).
- **parse+lint — S.** Already the strongest package at 78.35 %. The
  no-coverage sites are concentrated and shallow.
- **trust/inbox/verify/stamp — S.** 61 survivors total. `OkfStamp.cs`
  (66.86 %) is the only file needing real work.

## (a) No-coverage sites

393 mutants across 61 files. Grouped by judgement rather than by file, because
the judgement is what D needs.

### dead — unreachable or unused; delete rather than test

| Site | Mutants | Why dead |
| --- | --- | --- |
| `OkfSearchQuery.cs:145` `HasTerms` | 1 | `public bool HasTerms => this.terms.Count > 0;` has **no reference anywhere** — not in `src/`, not in `tests/`. Nothing can kill its mutants because nothing calls it. Delete. |
| `OkfConfig.cs:62` `IsGlobalLayer` | — | Assigned in the constructor at line 49, declared at 62, **never read** in `src/` or `tests/`. A property that only ever gets written. Delete both the property and the constructor parameter feeding it. |
| `OkfSiteAssets.cs:47-49` | 2 | The `?? throw new InvalidOperationException("The embedded site asset … is missing")` guard. It can only fire if `Okf.Core.csproj` stops declaring the `EmbeddedResource`, which is a build-time invariant — if it broke, every site test would fail first. Defensive, not reachable. File scores 0.00 % solely because of it. |
| `OkfStamp.cs:276-278, 293-295, 399-401` | 3 blocks | The `if (lines.Count == 0 \|\| lines[0].Trim() != FrontmatterDelimiter) return null;` and `if (fence < 0) return null;` guards in `TryStampGenerated` / `TryInsert`. Both callers parse the document through `OkfDocument` **before** splicing, so a file reaching here provably has both fences. Guard that cannot be false. Keep (AD-23 text surgery is worth the belt) but do not write tests for it. |
| `OkfStamp.cs:258, 356` | 2 | `catch (OkfDocumentException) { return false; }` in the already-stamped checks — same reasoning, the caller has already parsed successfully. |

Note that these are **defensive**, not accidental, and the AD-44 honest answer
is to record them as unkillable rather than to manufacture coverage. Only the
first two rows are deletions; the rest stay and should be excluded from the
score conversation.

### untested-reachable — a real CLI/MCP path, specified by a doc

| Site | Mutants | Path that reaches it | Spec |
| --- | --- | --- | --- |
| `RegistryArguments.cs:76-90` | 12 | `okf registry list --format json` / `--format=text`. The whole `--format` case is uncovered — parsed, documented, never exercised. | PRD §3 CLI surface row for `okf registry`; **AD-54** requires each verb's `CompletionTable` options to equal its parser's `case` labels, so this flag is asserted to exist by the completion suite while nothing tests it working. |
| `RegistryArguments.cs:111-128` | 12 | The `Next()` helper's `"Option '{option}' requires a value."` refusal, and the "at most one argument" error. | PRD CLI-2 (`okf register`/`unregister`/`registry`), exit 2 usage. |
| `McpCommand.cs:40-122` | 21 | `okf mcp` — unknown `--scope` value, unknown option, path-plus-scope exclusivity, and the whole `--verbose` resolution report. | **AD-30** (scope fixed at launch), **AD-51** (`okf mcp --scope`), PRD MCP-1/MCP-3. Exit 2 startup failure is in the §3 table. |
| `SiteCommand.cs:48-104` | 17 | `okf site --verbose` — every progress line, including the single-file vs multi-page branch. | **AD-40**, PRD §5. CLI-4 requires `--verbose` to report the effective value of anything that changed behaviour. |
| `IndexCommand.cs:51-56, 217-231` | 14 | `okf index --verbose` and the `--check` drift report's prose. | **AD-13/AD-14**, PRD CLI-10, ACC-3. |
| `LintCommand.cs:33-237` | 10 | `okf lint --verbose` resolution reporting, and the `--list-rules` table. | PRD CLI-15, CLI-4. |
| `BundleCommand.cs:57-241` | 11 | `okf bundle --verbose` and the `--verify` finding lines. | **AD-37**, PRD CLI-14. |
| `CaptureCommand.cs:87-321` | 14 | `okf capture add` / `close` refusal prose. | **AD-52**, PRD SKILL-3/SKILL-6, ACC-5. |
| `OkfLinter.cs:459-535` | 6 | Diagnostic message construction for three rules whose message text no test reads. | **AD-11**, PRD CLI-7. |
| `OkfLinter.cs:821` | 3 | `Text(source, "id") ?? Text(source, "resource") ?? "source"` — the fallback name in the source-drift message when a source has neither `id` nor `resource`. | **AD-11**. Reachable in principle via a foreign bundle (AD-4 reports, never blames), but §5.1 requires `id`, so this is closer to `unclear` — see below. |
| `SiteArguments.cs`, `IndexArguments.cs`, `SkillsArguments.cs`, `SearchArguments.cs`, `CaptureArguments.cs`, `BundleArguments.cs`, `UpgradeArguments.cs`, `GeneratedArguments.cs`, `InitArguments.cs`, `LintArguments.cs` | 66 | Argument-parser refusal branches across ten verbs — unknown option, missing value, duplicate positional. Every one is an exit-2 contract. | PRD §3 exit-code column for each verb; **AD-5** (exit codes are contract). |

### unclear — needs a human call in D

| Site | Mutants | Question |
| --- | --- | --- |
| `OkfEnvironment.cs:66-71` | 2 | The Windows `LOCALAPPDATA` branch of `DataDirectory`. Unreachable on the Linux test host, live on Windows. **AD-50** puts skills under `$XDG_DATA_HOME/okf`; the Windows equivalent ships untested on every platform CI runs. Either a Windows CI lane or an injectable `IsWindows` seam — a decision, not a test. |
| `OkfEnvironment.cs:104-117` | 9 | `FromProcess()` — reads the *real* process environment. Uncovered because every test injects a fake `OkfEnvironment` instead, which is the correct design. Only `Program.cs` calls it. Reachable by literally every real invocation and by no unit test; the `test-install` acceptance harness is the only thing that exercises it. |
| `OkfUpgrade.cs:698-741` | 5 | `TryDelete` and the `.old` retirement path. **AD-53** makes this Windows-only (POSIX overwrites in one move), so the `Retire` step never runs on Linux. Same call as `OkfEnvironment`'s Windows branch. |
| `OkfUpgrade.cs:203-205` | 1 | A block-removal in the redirect loop. AD-53 pins five hops and an https-only rule; worth confirming which branch this is before writing a test. |
| `OkfIndex.cs:179` `Count(status)` | 1 | `public int Count(OkfIndexStatus status)` has no caller in `src/` — `IndexCommand` counts per-index instead. Test-only API, like `HasDrift` and `ResourceNameFor`. Delete or keep as a documented query surface. |
| `OkfLinter.cs:821` | 3 | See above — the `?? "source"` fallback needs a ruling on whether a source with neither `id` nor `resource` is reachable through AD-4's foreign-bundle tolerance. |

### Where the docs are stale

**Nothing false was found.** Every claim in `docs/architecture.md` and
`docs/prd.md` that names a file, type or flag resolves to code that exists. Two
soft gaps, neither a wrong statement:

- `AD-54` describes completions as covering bash/zsh/fish/pwsh;
  `CompletionCommand.cs:84-87` also accepts `powershell` as an alias. A doc
  **omission**, not a stale claim.
- `docs/architecture.md:1136` holds `okf completion` to "each `*Arguments.Flags`",
  but `okf version` has no `VersionArguments`. `CliApplication.cs:143-145`
  already documents this as an inline exception, so it is disclosed.

The previous baseline's own text is the one thing now out of date: it named
`IndexArguments.cs` (9), `IndexCommand.cs` (15), `OkfLinter.cs` (13),
`OkfValue.cs` (11) and `McpToolset.cs` (9) as the no-coverage concentration and
said "`Program.cs` is 0 % with its single mutant uncovered". That last part is
still exactly true — `Program.cs` is 0 %, one no-coverage mutant, a block
removal at lines 11-14 — but the concentration has moved: the largest blocks are
now `RegistryArguments.cs` (24) and `McpCommand.cs` (21), both of which post-date
that run.

## (b) Survived mutants

755 actionable. The five worth doing first, then the classes.

### Top test gaps

| # | Site | Mutation | Test that kills it | Tag |
| --- | --- | --- | --- | --- |
| 1 | `OkfIndex.cs:504-524` | `bySubdirectory != 0` → `== 0`, `bySection != 0` → `== 0`, and both tiebreak blocks removed | Two index entries differing **only** in letter case in the same section, asserting the emitted order is stable and ordinal — the comment at line 515 says exactly this and nothing tests it. | test-gap |
| 2 | `OkfRegistry.cs:352-394` | `Indented = true` → `false`; `encoderShouldEmitUTF8Identifier: false` → `true` | Assert `registry.json`'s exact bytes after `okf register`: indented, no BOM, one trailing newline. **AD-51** says "written deterministically"; a BOM would ship and nothing notices. | test-gap |
| 3 | `OkfSearch.cs:327-328` | `1 - B` → `1 + B`; `B * len / avgLen` → `B * len * avgLen`; `(K1 + 1)` → `(K1 - 1)` | A fixture trading term frequency against document length so the true formula and each mutant disagree on **ranking**, not just score. | test-gap (the two constant-factor ones are `equivalent-mutant`, see below) |
| 4 | `LintText.cs:194-211` | `&&` → `\|\|` at 194 and 202; `Any` → `All` at 195; `slash + 1` → `slash - 1` at 211 | Link-classification table test: a value that is empty, one with whitespace, one starting `/`, one starting `./`, one with an extension after the last slash. Each mutant flips exactly one row. **AD-34**/**AD-45**. | test-gap |
| 5 | `FileLayout.cs:47-97` | `lines.Length > 0` → `>= 0`; `end < 0` → `<= 0`; `end + 1` → `end - 1`; `line.Length > key.Length` → `>=` | Frontmatter-boundary cases: a file whose frontmatter is the whole file, one with no trailing newline, one where a key name is a prefix of another. **AD-10** requires round-trip fidelity and these decide which lines are body. | test-gap |

### Runners-up worth naming

- `OkfSiteBuilder.cs:230-231` — the shared-prefix walk computing relative links
  between pages. `fromSegments.Length - 1` → `+ 1` survives; a two-deep
  cross-directory link fixture kills it. **AD-40**. `test-gap`.
- `OkfSiteBuilder.cs:292` — `previous < 0 ? parent : parent[(previous + 1)..]`
  survives three ways. A page in a nested directory with a parent segment.
  `test-gap`.
- `OkfRegistry.cs:459-466` — slug generation (`slug[^1] != '-'`, the `"vault"`
  fallback). **AD-51** pins "a slug of the directory name chosen once"; register
  a directory named `--` and one named `.` . `test-gap`.
- `OkfScope.cs:264,279` — `vaults.Count == 1 ? vaults[0] : null` and the
  `root`/`roots` pluralization in the resolution sentence. **AD-51**/CLI-1
  require resolution to be reported; assert the sentence for 0, 1 and 2 roots.
  `test-gap`.
- `OkfSearch.cs:371-373` — snippet anchoring (`start > 0` → `>= 0`, `>= start`
  → `> start`). A match at offset 0 versus offset 1. **AD-28**. `test-gap`.

### equivalent-mutant — do not chase

| Class | Count | Why equivalent |
| --- | --- | --- |
| `ThrowIfNull` / `ThrowIfNullOrEmpty` guards | 177 | Analyzer-mandated (CA1062, work item #31). Every caller is in-repo and none passes null, so removing the guard changes nothing observable. Killing them means writing null-argument tests that document the analyzer, not the product. |
| Diagnostic prose string literals | 616 | Where the message is a contract it is already pinned; where it is wording, pinning it verbatim is a change-detector test. Unchanged ruling from work item #11. |
| `OkfSearch.cs:328` `(K1 + 1)` → `(K1 - 1)` and the `*` before it → `/` | 2 | Both are a constant factor applied to every term, so no ranking can change. Recorded in work item #11 and still true. |
| `OkfSearch.cs:322-324` `frequency <= 0` → `< 0` and the `continue` → `;` | 2 | Between them they only let a zero-frequency term add zero. |
| `OkfRegistry.cs:416`, `OkfUpgrade.cs:280-282`, `OkfEnvironment.cs:66` | ~8 | `OperatingSystem.IsWindows()` / `IsMacOS()` ternaries. On the Linux test host the untaken branch is unreachable; these are platform-gated, not untested. A Windows CI lane would kill them — a decision for D, not a test. |
| `OkfCanonicalTimestamp.cs:45` `AssumeUniversal \| AdjustToUniversal` → `&` | 1 | Both styles are already implied for a `Z`-suffixed instant, which **AD-24** makes the only form okf writes. No input distinguishes them. |
| `OkfBundler.cs:545-546` `leaveOpen: true` → `false` | 2 | The `using` declarations dispose in the right order regardless; the stream is disposed either way. |

## Operational notes

- **21 m 47 s, not the ~8 m the mise task and CI comments still claim.** The
  `mutate` task description and the `mutation` CI job both say "~8 minutes on 8
  cores for ~3400 mutants". It is now 10 986 mutants and 22 minutes. The job's
  `timeout: 2h` still covers it with room, but the comments are stale by a
  factor of three and should be corrected when D touches them.
- **No flaky mutants and no timeouts worth the name.** 21 timeouts out of
  10 986 (0.19 %), all in `Okf.Core`, and `Okf.Cli` had none. Errors: 0.
- **2639 compile errors (24 % of all mutants) are expected, not a fault.**
  Stryker's "safe mode" removes every mutation in a method it cannot compile —
  three `CS0165 use of unassigned local` cases in `OkfConfig.ReadSeverity`,
  `OkfCaptureManifest.ReadEntry` and `OkfCaptureManifest.ReadLint` each take
  their whole method's mutants with them. Those three methods are effectively
  unmutated, so their real coverage is unmeasured either way.
- **The console summary and the JSON report disagree by 53 mutants.** The
  run's stdout printed `Killed: 5237 / Survived: 1601`; the JSON reports 5290
  killed and 1548 survived. Both agree on the total (6838) and only the JSON's
  split reproduces the 73.23 % Stryker itself printed, so **the JSON is
  authoritative** and the console line is a mid-run snapshot. Read the report,
  not the terminal.

## D and E outcomes — trust/inbox/verify/stamp

Work item #13, branch `13-trust-inbox`. Stryker over the package's seven files
alone: **76.52 % → 86.67 %** (killed 251 → 273; survived + no-coverage 77 → 42).
`OkfActor.cs`, `OkfInbox.cs`, `OkfLifecycleInstant.cs` and `OkfTrustTier.cs`
reach 100 %, `OkfStamp.cs` 66.86 % → 82.21 % and `OkfVerifyIdentity.cs`
72.92 % → 73.91 %. The after-figures are read from the JSON report, not the
console line, for the reason recorded above.

**D found nothing to delete.** Every public member of the seven files has a
caller in `src/` or is one of the two model-level entry points
(`OkfStamp.Verify`, `OkfStamp.StampGenerated`) the class documents as the
library surface beside the text ones. No doc claims a path that is not there.

**Recorded as unkillable by construction**, so the score conversation should
exclude them rather than manufacture coverage (AD-44):

| Site | Why no input distinguishes the mutant |
| --- | --- |
| `OkfStamp.cs:275, 379` and the `276`, `380`, `399` blocks | `text.Split('\n')` never yields an empty list, so `lines.Count == 0` is already dead; the rest of the guard only ever routes a fenceless file to the emitter, which is where the mutant's own output ends up too. |
| `OkfStamp.cs:282, 388` | `fence` is overwritten whenever a closing fence exists, and when none does both the guard and its mutant end at the emitter's `Unterminated YAML frontmatter block`. |
| `OkfStamp.cs:292, 300, 398, 408` | `fence` is `-1` or `≥ 1` and `key` is `-1` or `≥ 1`; neither can be `0`, so `< 0` and `<= 0` agree on every reachable value. |
| `OkfStamp.cs:310, 419` | The closing fence is itself a non-whitespace line, so `i <= fence` assigns `stop` the value it already held. |
| `OkfStamp.cs:454, 455` | The surgery this mutant lets through turns a block-style bare mapping into a list without re-indenting the `at:` line under it, so the result does not read back as one more event and the parse-back check hands it to the emitter regardless. Checked by hand: the two outputs are byte-identical. |
| `OkfStamp.cs:118` | Dropping the initializer leaves `OkfCollectionStyle.Any`, and YamlDotNet writes a non-empty root-level sequence in block style either way. The list is never emitted empty — the event is added before the document is serialized. |
| `OkfStamp.cs:87-89, 191-193` | AD-23's belt: the emitter fallback cannot lose the event it just added, so the "did not read back" refusal has no reachable input. Keep the check. |
| `OkfStamp.cs:258, 356` | Already recorded above: the caller parsed the document first. |
| `OkfCanonicalTimestamp.cs:45` | `IsCanonical` discards the parsed value and its format string carries a literal `Z`, so no `DateTimeStyles` combination changes the boolean it returns. |
| `OkfVerifyIdentity.cs:59` (`GIT_CONFIG_SYSTEM`, `GIT_CONFIG_NOSYSTEM`) | `git config --global --get` does not read the system file, so passing these through changes no answer. They are hermeticity belt for a future non-`--global` read. `HOME`, `XDG_CONFIG_HOME` and `GIT_CONFIG_GLOBAL` are each pinned by a test now. |
| `OkfVerifyIdentity.cs:112` | `CreateNoWindow` has no effect on the POSIX test host. |
| `OkfVerifyIdentity.cs:137` | Draining stderr only matters for an output large enough to fill the pipe, which `git config` cannot produce. |

**Killable, and not killed on purpose.** An adversarial pass over the table
above found that four of the sites first recorded as unkillable are not, so they
are moved here: calling a mutant unkillable and calling it not worth a test are
different claims and only the second one is true of them.

| Site | Input that distinguishes the mutant | Why no test |
| --- | --- | --- |
| `OkfStamp.cs:319, 320` and `OkfStamp.cs:435, 436` | A comment line indented under `generated:` or `verified:` whose value is a one-line flow mapping. It makes `region` non-empty in a document that parses, which is the case the original rows assumed away. | The mutants' output is *better*: they keep the comment, while the original sends the document to the emitter, which drops it. A test here would freeze that loss into the suite. It is a behaviour decision first — see below. |
| `OkfStamp.cs:334` and `OkfVerifyIdentity.cs:91, 181, 188` | Any assertion over the whole diagnostic string. | These are the explanatory halves of messages whose actionable clause is already asserted with `Contains`. Pinning whole sentences buys a test edit per wording change and no behaviour. |

**Frontmatter comments are dropped whenever stamping falls to the emitter.**
Found while disproving the rows above, present on `dev`, not introduced here:
`OkfStamp.VerifyText` on a concept carrying a YAML comment in its frontmatter
returns the emitter's re-serialization, and the comment is gone. That is the
whole-file-diff harm AD-23's insertion path exists to avoid, arriving through
the fallback instead. Needs a decision — widen the surgery to tolerate comment
lines, or accept the loss and say so in the AD — before it gets a test.

**Left for a decision, not a test:** `OkfVerifyIdentity.cs:132, 143, 151-152` —
`Process.Start` returning null, the five-second timeout kill, and the
"git is not installed" catch. Reaching any of them needs an injectable process
seam, which is a public API change the 1.x freeze does not allow, or a git-less
CI lane. Same class as `OkfEnvironment`'s Windows branch.

**AD-25 needed no new tests.** `OkfLifecycleInstant.cs` was already at 100 %:
the coarser-precision comparison, the written-date rule and the wider drift
ordering each have a test whose expected values come from the AD text.
