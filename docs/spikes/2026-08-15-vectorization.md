# Spike: sqlite-vec vectorization as a search fallback

Work item #8. Dated 2026-08-15, run on `8-vectorization-spike` at `d1ccbc3`.
Everything below was measured on this machine, not argued: the throwaway project
under `spikes/vectorization/` is the apparatus, and every number has the command
that produced it.

## Recommendation

**GO-with-conditions.** Confidence: high on the mechanics, medium on the product
value.

The mechanics are better than expected. sqlite-vec works from .NET, every package
in the graph is permissively licensed, and a NativeAOT publish of a console app
using `Microsoft.Data.Sqlite` + the sqlite-vec extension produces **zero trim/AOT
warnings and a binary that runs** — including with ONNX Runtime in the graph. The
Q10 fallback clause (drop to trim-safe self-contained non-AOT) is **not** needed.

Three conditions, in order of how much they need Ringo:

1. **The single-file story breaks, and only Ringo can price that.** `okf` today is
   one 5.19 MiB file. Adding sqlite-vec makes it **three files**: the binary grows
   ~1.56 MiB and two native sidecars appear (`libe_sqlite3.so` 1.40 MiB, `vec0.so`
   148 KiB). Nothing about NativeAOT can embed them — a loadable SQLite extension
   is by construction a separate `dlopen`-able object. `curl | sh` still works
   (ship a tarball, not a bare binary), but "one file you can scp anywhere" is
   over unless the extension is statically linked from source (untested, see
   below).
2. **The sqlite-vec NuGet package is a stale third-party alpha.** `sqlite-vec
   0.1.7-alpha.2.1`, published **2025-05-09**, authored "Alex Garcia" but *owned*
   by `jeffhandley`; there is no `bindings/dotnet` in `asg017/sqlite-vec` at all.
   Upstream is at `v0.1.9` (stable, 2026-03-31) and `v0.1.10-alpha.4`
   (2026-05-18) — the package is ~15 months and two minor releases behind. For a
   project whose own bundle preaches a custodian model, taking a build of a native
   `.so` from an unrelated owner needs a deliberate ruling.
3. **The embedder source is Ringo's decision and the spike does not make it.**
   Evaluated below; the demo was built on the local ONNX option because it was the
   only one reachable offline from this sandbox.

The product value is the medium-confidence half. The rescue is **real and
demonstrable** — see the `steward` → *The Custodian Model* result — but it is
narrow: it fires only when a query term appears nowhere in the corpus. On
multi-word natural-language queries the existing BM25 engine's OR-fallback is
already as good as or better than cosine. So this buys vocabulary-mismatch
insurance, not a better search engine, and it should be built as insurance.

## What was built

`spikes/vectorization/` — a console app deliberately **outside `Okf.sln`**, so
`mise run build`, `mise run test`, `mise run mutate` and the CI `licenses` job
(which runs `dotnet CycloneDX Okf.sln`) cannot see it. Its packages therefore
never enter the SBOM and cannot trip or pass the license gate by accident; the
licenses below were verified by hand instead. It project-references `Okf.Core`, so
the vector index is built over the same corpus `okf search` reads and both can be
run in one process.

Suite after the spike: **817 tests pass** (556 `Okf.Core.Tests` + 261
`Okf.Cli.Tests`), `mise run build` and `mise run test` both green, unchanged.

## Finding 1 — sqlite-vec from .NET works, on a package you should read twice

### The package

| Item | Value |
| --- | --- |
| Package | `sqlite-vec` `0.1.7-alpha.2.1` (nuget.org, published 2025-05-09) |
| Contents | `runtimes/{linux-x64,linux-arm64,osx-x64,osx-arm64,win-x64}/native/vec0.*`, plus a `contentFiles` one-liner adding `SqliteConnection.LoadVector()` |
| `vec0.so` (linux-x64) | 151,320 bytes |
| Declared license | MIT (`<license type="expression">MIT`) |
| Upstream license | Dual **Apache-2.0 / MIT** — `LICENSE-APACHE` and `LICENSE-MIT` both sit in `asg017/sqlite-vec` root |
| Official .NET binding? | **No.** `asg017/sqlite-vec/bindings/` contains `go`, `python`, `rust` only |

The alternative to the package is loading a `vec0.so` you obtained yourself:
upstream publishes per-platform `*-loadable-*.tar.gz` **and** `*-static-*.tar.gz`
**and** an amalgamation `.c` for every release, with a `checksums.txt`. That is the
supply-chain-clean path and it is the same `LoadExtension` call either way — the
spike's `VecStore.ResolveVec0Path()` already prefers a `vec0.so` sitting beside
the binary over the package's copy.

### The API, as it actually behaves

Loading works through `Microsoft.Data.Sqlite` with no ceremony:

```csharp
connection.EnableExtensions(true);
connection.LoadExtension("vec0");   // or an absolute path
```

Verified in-process: SQLite **3.53.3**, `vec_version()` = **`v0.1.7-alpha.2.1`**.

Two behaviours a production writer must design around, both found the hard way:

- **`vec0` does not support UPSERT.** `INSERT … ON CONFLICT … DO UPDATE` on the
  virtual table raises `SQLite Error 1: 'UPSERT not implemented for virtual table
  "concept_vectors"'`. Re-embedding is `DELETE` then `INSERT`.
- **KNN is a `MATCH` + `k =` constraint**, not `ORDER BY … LIMIT`:

  ```sql
  SELECT c.path, v.distance
  FROM concept_vectors v JOIN concepts c ON c.id = v.concept_id
  WHERE v.embedding MATCH $query AND k = $k
  ORDER BY v.distance;
  ```

Cosine distance is exact. `smoke` inserts three unit vectors and queries with
`[1,0,0,0]`:

```text
sqlite     3.53.3
sqlite-vec v0.1.7-alpha.2.1
  a.md     distance=0.0000 cosine=1.0000
  c.md     distance=0.2929 cosine=0.7071
  b.md     distance=1.0000 cosine=0.0000
```

`0.2929 = 1 − cos 45°`, to four places. The metric is what it says it is.

### Licenses verdict for the spike graph

Read from each package's `.nuspec` **at the exact resolved version**, not from
the project's docs:

| Package | Resolved | License | Source |
| --- | --- | --- | --- |
| `sqlite-vec` | 0.1.7-alpha.2.1 | MIT | SPDX expression in nuspec |
| `Microsoft.Data.Sqlite` | 10.0.11 | MIT | SPDX expression |
| `Microsoft.Data.Sqlite.Core` | 10.0.11 | MIT | SPDX expression |
| `SQLitePCLRaw.core` | 2.1.12 | Apache-2.0 | SPDX expression |
| `SQLitePCLRaw.bundle_e_sqlite3` | 2.1.12 | Apache-2.0 | SPDX expression |
| `SQLitePCLRaw.provider.e_sqlite3` | 2.1.12 | Apache-2.0 | SPDX expression |
| `SQLitePCLRaw.lib.e_sqlite3` | 2.1.12 | Apache-2.0 | SPDX expression |
| `Microsoft.ML.Tokenizers` | 2.0.0 | MIT | SPDX expression |
| `Microsoft.ML.OnnxRuntime` | 1.29.0 | **license file, not SPDX** | `<license type="file">LICENSE`; upstream `microsoft/onnxruntime` is MIT |

**Verdict: the sqlite-vec half of the graph is clean and needs no allowlist
change.** Every identifier is already in `scripts/licenses-allowed.json`. SQLite
itself is public domain and is not an SBOM component.

Two traps to record before anyone promotes this:

- `Microsoft.ML.OnnxRuntime` declares a license *file*, so CycloneDX records
  "Unknown - See URL" and `scripts/check-licenses.py` would fail it. It would need
  a `scripts/license-overrides.json` entry (MIT, evidenced by the upstream
  `LICENSE`) — exactly the `xunit.abstractions` pattern already there.
- `SQLitePCLRaw.lib.e_sqlite3` **3.x** (version `3.53.3`) also switched to a
  license file. Today `Microsoft.Data.Sqlite` 10.0.11 pulls `2.1.12`, which is a
  clean SPDX `Apache-2.0`. A future bump to SQLitePCLRaw 3.x reintroduces the
  override requirement.

## Finding 2 — AOT: pass, at the cost of the single-file story

**Verdict: zero trim/AOT warnings, in every configuration tried, and the published
binary runs.** This was the crux and it is not a blocker.

Sizes are isolated with two minimal probes so the delta attributable to
sqlite-vec is not confounded by the rest of the spike (both probes are reproduced
verbatim in the appendix):

| Publish (`-c Release -r linux-x64`, `PublishAot=true`) | Managed binary | Native sidecars | Files | Warnings |
| --- | --- | --- | --- | --- |
| Hello-world baseline | 1,147,408 B (1.09 MiB) | — | 1 | 0 |
| \+ `Microsoft.Data.Sqlite` + `sqlite-vec` | 2,788,192 B (2.66 MiB) | `libe_sqlite3.so` 1,470,784 B, `vec0.so` 151,320 B | 3 | 0 |
| Full spike (Okf.Core, sqlite-vec, HTTP+JSON) | 8,538,312 B | same two | 3 | 0 |
| Full spike **+ ONNX Runtime** | 9,540,696 B | \+ `libonnxruntime.so` 28,497,752 B, `libonnxruntime_providers_shared.so` 14,632 B | 5 (+2 stray Windows DLLs) | 0 |
| `okf` today, for reference | 5,447,176 B (5.19 MiB) | — | 1 | 0 |

**The measured cost of adding sqlite-vec to a NativeAOT binary:**

- in-binary: **+1,640,784 B (+1.56 MiB)**
- on disk: **+3,262,888 B (+3.11 MiB)**, and **1 file becomes 3**

Projected onto `okf`: **5.19 MiB → ~6.75 MiB of binary plus 1.55 MiB of sidecars,
≈ 8.3 MiB in three files.**

### How the native library ships

Bundled in the NuGet package as a per-RID `runtimes/<rid>/native` asset, flattened
beside the binary by `dotnet publish -r <rid>`. Nothing is downloaded at runtime.
Extension resolution is **cwd-independent** — the AOT binary was run from an
unrelated directory and still loaded `vec0.so` from its own directory.

`libe_sqlite3.so` (1.40 MiB) is the larger sidecar and it is not sqlite-vec's
fault: it is SQLitePCLRaw's bundled SQLite build. Two ways to remove it, neither
verified here:

- **`SQLitePCLRaw.bundle_sqlite3`** links the OS's `libsqlite3` instead. Removes
  1.40 MiB, adds a system dependency, and is a real risk: macOS's system SQLite
  ships with extension loading **disabled**, which would silently kill vec0 on a
  platform okf already targets.
- **Static linking.** Upstream sqlite-vec publishes `*-static-*.tar.gz` (`.a`) and
  an amalgamation for every release, and ILCompiler supports `DirectPInvoke` +
  `NativeLibrary`. Statically linking *both* vec0 and e_sqlite3 would restore the
  single file. `SQLitePCLRaw.lib.e_sqlite3` ships `.a` files only for
  `browser-wasm`, so the SQLite half means building e_sqlite3 yourself.
  **Untested — do not budget for it without a second spike.**

### The ONNX AOT result is worth recording separately

`Microsoft.ML.OnnxRuntime` 1.29.0 also publishes AOT-clean (0 warnings) and the
resulting native binary **runs inference correctly**, producing cosine scores
byte-identical to the JIT build (`0.1426` for `steward` → *The Custodian Model*
under both). That is a genuinely good result — but it costs 27 MiB of native
runtime, and the publish also drops **`onnxruntime.dll` (16,149,344 B) and
`onnxruntime_providers_shared.dll`** into a `linux-x64` output. Windows binaries
in a Linux publish is a packaging bug in the ONNX Runtime package; it would have
to be pruned explicitly.

## Finding 3 — embedders: evaluation, not a decision

### The environment constrained this

No OpenAI-compatible endpoint was reachable from this sandbox. `roci` resolves and
answers on `:8080`, but that is **AdGuard Home sync**, not inference; `:11434`
(Ollama), `:8000`, `:1234`, `:5000`, `:3000` all refuse connections. So option (b)
could not be exercised against a real model. `huggingface.co` *was* reachable, so
**option (a) was built for real** and carries the demo.

### Side by side

| | (a) local ONNX | (b) HTTP `/v1/embeddings` | (c) Ollama-style daemon |
| --- | --- | --- | --- |
| Dependency weight | `Microsoft.ML.OnnxRuntime` + `Microsoft.ML.Tokenizers`; **+27 MiB native runtime, +1 MiB binary**, plus a ~90 MB model file | **Zero packages.** `HttpClient` + one source-generated JSON contract | Zero packages (it is option (b) with a different base URL) |
| AOT | **Verified: 0 warnings, runs** | **Verified: 0 warnings, runs** (source-gen JSON keeps it reflection-free) | Same as (b) |
| Offline | **Yes, fully.** Model is a local file | No — feature simply unavailable, which is a clean degradation given the design is opt-in | Yes if the daemon is local, no if it is on `roci` and you are not |
| Distribution | The hard part: a 90 MB model cannot ship in a 5 MiB CLI. It is a download-on-first-use or a user-supplied path. New failure modes (partial download, checksum, cache location) | Nothing to distribute. Endpoint + model name in config; **no API key baked** — read from env | Nothing to distribute, but the user must install and run a daemon |
| Determinism | Reproducible *for a fixed model file*. Different model, different vectors | Worse: the endpoint can silently change model versions under you | Same as (b) |
| Cost | CPU only; 659 ms for 21 concepts | Whatever the endpoint charges; 21 HTTP round trips per full index | Free, local CPU/GPU |

Measured throughput, all-MiniLM-L6-v2 through ONNX Runtime, NativeAOT binary, 21
concepts:

```text
full index          659 ms   (21 embedded)
no-op reindex         0 ms   (0 embedded, 21 unchanged)
one concept edited   30 ms   (1 embedded, 20 unchanged)
```

Query wall clock, whole process, three runs each:

```text
vector (okf-spike-vec query, ONNX)   251 / 241 / 248 ms
lexical (okf search, AOT)             10 /  11 /  11 ms
```

~22× per query, dominated by ONNX session startup rather than by sqlite-vec. This
is a strong argument for the recall-rescue trigger: you pay it only when lexical
already came back empty.

The HTTP path was proven end-to-end against `mock-embeddings-server.py` (a stub
that speaks the protocol and returns hash vectors): 21 concepts in **29 ms** over
loopback from the NativeAOT binary. One transport gotcha worth writing down —
.NET's `PostAsJsonAsync` sends `Transfer-Encoding: chunked` with no
`Content-Length`, which a naive server implementation will reject.

### The demo: a query lexical misses and a concept plainly answers

The dogfood bundle has a concept titled **The Custodian Model**
(`okf/bundles/okf-net/practices/custodian-model.md`). The word "steward" appears
nowhere in the vault. Real `okf search`:

```console
$ okf search steward okf/
Found 0 results in 21 concepts across 1 bundle.
```

The spike's `compare`, which runs `OkfSearchEngine.Search` and the vector index
over the same corpus in one process:

```text
query: steward
lexical (okf search, BM25) — 0 match(es), mode=All
  (nothing)
vector (sqlite-vec, cosine, onnx:all-MiniLM-L6-v2)
  0.1426  okf-net/practices/custodian-model.md              The Custodian Model
  0.1055  okf-net/practices/about.md                        About the Practices Domain
  0.1052  okf-net/format/trust-tiers-and-acknowledgment.md  Trust Tiers and the Acknowledgment Model
```

Three more zero-lexical-hit queries, same index:

| Query | Lexical | Vector top hit | Right? |
| --- | --- | --- | --- |
| `trustworthy` | 0 results | 0.3967 Trust Tiers and the Acknowledgment Model | yes |
| `curator` | 0 results | 0.2188 The Custodian Model | yes |
| `gatekeeper` | 0 results | 0.2695 Trust Tiers and the Acknowledgment Model | defensible |
| `semver` | 0 results | 0.1724 About This Bundle | **no** |

`semver` is the honest counter-example and it is instructive: *Release and
Versioning* does not appear anywhere in the top eight. Four rescues out of five is
what this looks like on 21 documents, and the failure is silent.

Note also the calibration problem visible in that table: a **correct** top hit
scores 0.1426 (`steward`) while an **incorrect** one scores 0.1724 (`semver`).
Cosine similarity here is not comparable across queries, so no fixed relevance
threshold can be chosen from this evidence.

### The control: a fake embedder cannot do this

`--embedder fake` (deterministic token hashing, the same trick a naive
implementation reaches for) on the same query:

```text
query: steward
lexical (okf search, BM25) — 0 match(es), mode=All
  (nothing)
vector (sqlite-vec, cosine, fake-hash-384-v1)
  0.0000  okf-net/toolset/about.md         About the Toolset Domain
  0.0000  okf-net/practices/about.md       About the Practices Domain
  0.0000  okf-net/about-this-bundle.md     About This Bundle
```

Every score is 0.0000, because hashed tokens share no subspace. This is why the
demo needed a real model, and it is included so nobody mistakes plumbing for
semantics later.

### Where lexical is already fine

Run on multi-word natural-language queries, the existing BM25 OR-fallback holds
its own:

| Query | Lexical top hit | Vector top hit |
| --- | --- | --- |
| "shipping knowledge to someone who does not have the repo" | 6.7734 **Bundling and Distribution** | 0.3725 **Bundling and Distribution** |
| "who is allowed to edit shared knowledge" | 3.7615 The Custodian Model (rank 2; rank 1 was Bundling) | 0.2523 Equipping Agents for the Real World |
| "can I still believe an old note" | 4.3593 Tagging Discipline (wrong) | 0.2175 Provenance — Capture Versus Cite |

One clear vector win, one clear lexical win, one tie. **There is no measured case
for replacing or reweighting the lexical ranking** — which is the whole basis for
the design below.

## Finding 4 — integration design (design only)

### Index location: `<vault-root>/.okf/vectors.sqlite`

A dot-directory, deliberately, because that choice costs **zero new code in the
bundler**: `OkfBundle.ContentFiles()` — the single walk the bundler packages from
— already excludes dotfiles and dot-directories, and the bundler only packages
under `bundles/`, so a vault-root dot-directory is doubly invisible. It needs one
`.gitignore` line and nothing else. `okf index --check`'s drift gate must
explicitly ignore it, since that gate's exit code is a CI contract.

### Invalidation: per-file sha256, reusing the capture-manifest convention

The spike stores `(bundle, path, title, sha256)` beside the vectors and skips any
concept whose sha256 still matches. Measured: full 659 ms, no-op **0 ms**, one
edited concept **30 ms**. Two more invalidations are needed that the spike only
designs:

- **Embedder identity** (`onnx:all-MiniLM-L6-v2`, `http:<model>`) stored in the
  index. If it differs from the configured one, the whole index is stale — cosine
  distance between two models' outputs is noise, not a smaller number.
- **Deletions**: a concept in the index with no file on disk must be removed;
  sha256 comparison alone never notices.

### How it sits behind the search contract: recall-rescue, not fusion

**Recommendation: recall-rescue only.** When, and only when, lexical returns zero
results, run the vector query and return its hits.

The reasoning is the measurement, not taste:

1. **Fusion needs a weight and the data does not supply one.** BM25 scores here
   range 2–7 and are unbounded; cosine is in [−1, 1] and, per the calibration
   result above, not comparable across queries. Any fusion constant would be
   fitted to 21 documents.
2. **Fusion would have to be able to make results worse.** On the three
   natural-language queries, lexical won once, lost once, tied once. A blend has
   negative expected value at this evidence level; a rescue that only fires on an
   empty result set has a **floor of zero** — it can add a wrong answer where
   there was none, but it can never demote a right one.
3. **It is the cheapest thing that preserves the contract.** In the rescue case
   the result set is entirely semantic, so no incomparable scores are ever mixed
   inside one list. `score` stays opaque and higher-is-better exactly as Q7
   requires.
4. **It costs nothing on the hot path.** The 22× query penalty is only paid on
   queries that already failed.

The `UsedFallback` case (`matchMode == any`) is the tempting phase two: it is
lexical admitting it could not AND all the terms. Deliberately **not** recommended
for the first slice — that is where fusion sneaks back in, and it needs evidence
this spike does not have.

One contract decision falls out and belongs to Ringo: the rescue set needs to be
labelled. Either `matchMode: "semantic"` — clean, honest, but an additive value in
an enum Q7 fixed at `all`/`any`/`filter` — or a per-result boolean plus an outcome
field, which keeps the enum frozen at the cost of a wordier record.

### Opt-in surface

- Config key in `okf.json`, **default off**, e.g. `"search": { "vectors":
  "off" | "rescue" }` plus embedder settings. Never a flag-only feature: a
  flag would let a hook turn it on.
- **`okf mcp`'s `search` tool is unchanged** — it renders the same result set,
  so it inherits the rescue with no schema change.
- Because the index is gitignored and the config default is off, CI and the
  pre-commit hook can never see a semantic result. That is what keeps topic 6's
  determinism rule intact rather than merely documented.

### The determinism caveat, surfaced

Vectors are non-deterministic across embedder versions, so this can never be a
team default. Concretely:

- the index records its embedder identity and `okf search` prints a one-line
  notice on any run that used the rescue, naming it;
- `okf.json`'s vector settings are documented as **personal**, in the same breath
  as the rule that a shared config must not carry them;
- the index is never authoritative and never shipped: it is a generated artifact
  the bundler already cannot see, and a vault is complete without it.

## Smallest viable first slice

1. `okf index --vectors` writes `<vault>/.okf/vectors.sqlite` — sha256-gated,
   embedder identity recorded, deletions pruned.
2. `okf search` consults it only when the config enables it **and** the index
   exists **and** lexical returned zero results; results labelled and capped at
   the existing `--limit`.
3. One embedder, whichever Ringo picks. Behind an interface, so the second one is
   a class, not a redesign.
4. Docs: the determinism caveat, and the fact that a bundle never depends on this.

No fusion, no `UsedFallback` trigger, no MCP change, no default-on, no second
embedder. Everything above is phase two and should be justified by usage, not by
this spike.

## Open decisions only Ringo can make

1. **Embedder source.** The big one. (a) local ONNX buys true offline and
   reproducibility for 27 MiB of runtime plus a 90 MB model to distribute;
   (b) HTTP buys a zero-dependency binary and costs offline availability and
   version stability. The spike has no opinion it earned.
2. **Is three files acceptable?** `curl | sh` still works via a tarball, but
   decisions §3 chose .NET partly *for* single-binary distribution. If the answer
   is "one file or nothing", the next step is a static-linking spike, not this
   feature.
3. **The sqlite-vec `.so` provenance.** Third-party 15-month-old alpha NuGet
   package, or vendor upstream's checksummed release artifact and load it
   explicitly?
4. **`matchMode: "semantic"` versus a per-result flag** — a Q7 contract amendment
   either way.
5. **Does a four-out-of-five rescue rate justify shipping it at all?** The
   `semver` miss is silent and confident-looking. NO-GO on the product remains a
   defensible reading of the same evidence.

## Appendix — reproducing everything

```sh
# Finding 1: sqlite-vec plumbing
dotnet run --project spikes/vectorization -- smoke

# Finding 2: AOT, spike project
dotnet publish spikes/vectorization -c Release -r linux-x64 -o /tmp/aot
dotnet publish spikes/vectorization -c Release -r linux-x64 -p:SpikeOnnx=true -o /tmp/aot-onnx

# Finding 3: the model, then the demo
mkdir -p spikes/vectorization/models/all-MiniLM-L6-v2
cd spikes/vectorization/models/all-MiniLM-L6-v2
curl -sSLO https://huggingface.co/sentence-transformers/all-MiniLM-L6-v2/resolve/main/onnx/model.onnx
curl -sSLO https://huggingface.co/sentence-transformers/all-MiniLM-L6-v2/resolve/main/vocab.txt
cd -

/tmp/aot-onnx/okf-spike-vec index --keep --vault okf/ \
  --embedder onnx --onnx-dir spikes/vectorization/models/all-MiniLM-L6-v2 \
  --db /tmp/vectors.sqlite

/tmp/aot-onnx/okf-spike-vec compare "steward" --vault okf/ \
  --embedder onnx --onnx-dir spikes/vectorization/models/all-MiniLM-L6-v2 \
  --db /tmp/vectors.sqlite --limit 3

# The HTTP transport, against the stub
python3 spikes/vectorization/mock-embeddings-server.py &
/tmp/aot/okf-spike-vec index --vault okf/ --embedder http \
  --endpoint http://127.0.0.1:8477/v1/embeddings --model stub --db /tmp/http.sqlite
```

The two AOT size probes are throwaway and were not committed. Verbatim:

```sh
mkdir -p /tmp/probe/base /tmp/probe/sqlite
cat > /tmp/probe/base/base.csproj <<'EOF'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable><PublishAot>true</PublishAot>
    <InvariantGlobalization>true</InvariantGlobalization><EventSourceSupport>false</EventSourceSupport>
  </PropertyGroup>
</Project>
EOF
echo 'System.Console.WriteLine("base");' > /tmp/probe/base/Program.cs

sed 's#</PropertyGroup>#</PropertyGroup><ItemGroup><PackageReference Include="Microsoft.Data.Sqlite" Version="10.0.11" /><PackageReference Include="sqlite-vec" Version="0.1.7-alpha.2.1" /></ItemGroup>#' \
  /tmp/probe/base/base.csproj > /tmp/probe/sqlite/sqlite.csproj
cat > /tmp/probe/sqlite/Program.cs <<'EOF'
using Microsoft.Data.Sqlite;
using var c = new SqliteConnection("Data Source=:memory:");
c.Open();
c.EnableExtensions(true);
c.LoadExtension(System.IO.Path.Combine(System.AppContext.BaseDirectory, "vec0.so"));
using var cmd = c.CreateCommand();
cmd.CommandText = "CREATE VIRTUAL TABLE v USING vec0(id INTEGER PRIMARY KEY, e FLOAT[4] distance_metric=cosine); SELECT vec_version();";
System.Console.WriteLine(cmd.ExecuteScalar());
EOF

for p in base sqlite; do (cd /tmp/probe/$p && dotnet publish -c Release -r linux-x64 -o out); done
find /tmp/probe/*/out -type f ! -name '*.dbg' -printf '%10s  %p\n' | sort -rn
```

Index file sizes, for the record: **1,622,016 bytes at 21 concepts** and
**1,630,208 bytes at 53 concepts** — vec0 preallocates a full chunk (default 1024
rows), so the floor is ~1.55 MiB regardless of corpus size and grows by ~8 KiB per
32 concepts thereafter. Small vaults pay a fixed 1.5 MiB; that is fine for a
gitignored artifact and would not be fine for anything shipped.
