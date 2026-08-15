# Vectorization spike (work item #8)

**This is a throwaway spike, not shipping code.** It exists to answer the
questions in `docs/spikes/2026-08-15-vectorization.md` with measurements instead
of argument. Nothing here is tested, hardened, or covered by the mutation gate,
and none of it should be promoted into `Okf.Core` as-is.

## Why it is not in `Okf.sln`

`mise run build`, `mise run test`, `mise run mutate` and the CI `licenses` job all
target `Okf.sln`. Leaving this project out of the solution means:

- its packages never reach the CycloneDX SBOM, so they cannot pass or fail the
  `licenses` gate by accident (the licenses of everything used here are verified
  by hand in the spike report instead);
- an AOT-hostile or copyleft dependency tried out here cannot leak into the
  shipped `okf` binary;
- `dotnet test Okf.sln` stays exactly as green as it was before the spike.

It references `..\..\src\Okf.Core\Okf.Core.csproj` so the vector index is built
over the same corpus the real `okf search` sees, and so the two can be compared
in one process.

## Building and running

The ONNX embedder is behind an opt-in property, because `Microsoft.ML.OnnxRuntime`
drags ~44 MB of native runtime into every build and publish:

```sh
dotnet build spikes/vectorization                      # sqlite-vec only
dotnet build spikes/vectorization -p:SpikeOnnx=true    # + local ONNX embedder
```

Commands:

```sh
# 1. sqlite-vec plumbing: create a vec0 table, insert vectors, KNN query
dotnet run --project spikes/vectorization -- smoke

# 2. embed a vault (default embedder is the deterministic fake)
dotnet run --project spikes/vectorization -- \
  index --vault okf/ --db /tmp/vectors.sqlite

# 3. lexical (real okf search) versus vector, side by side
dotnet run --project spikes/vectorization -- \
  compare "steward" --vault okf/ --db /tmp/vectors.sqlite
```

Add `--keep` to `index` for the incremental path: a concept whose sha256 already
matches the stored one is not re-embedded.

## The three embedders

| `--embedder` | What it is | Honest about |
| --- | --- | --- |
| `fake` (default) | Deterministic token hashing, 384d | Proves storage and query plumbing only. Hashed tokens share no subspace, so it can **never** match a synonym — `steward` scores 0.0000 against every concept. |
| `onnx` | `all-MiniLM-L6-v2` through ONNX Runtime, 384d | Real semantics. Needs `--onnx-dir` pointing at a directory holding `model.onnx` and `vocab.txt`, and a build with `-p:SpikeOnnx=true`. |
| `http` | POST to an OpenAI-compatible `/v1/embeddings` | Real if the endpoint is. `mock-embeddings-server.py` is a stub that speaks the protocol with fake vectors, for testing the transport alone. |

Fetching the model (~90 MB, not committed, `.gitignore`d):

```sh
mkdir -p spikes/vectorization/models/all-MiniLM-L6-v2
cd spikes/vectorization/models/all-MiniLM-L6-v2
curl -sSLO https://huggingface.co/sentence-transformers/all-MiniLM-L6-v2/resolve/main/onnx/model.onnx
curl -sSLO https://huggingface.co/sentence-transformers/all-MiniLM-L6-v2/resolve/main/vocab.txt
```

## Determinism warning

Vectors are only comparable to other vectors from the **same embedder build**.
Swapping the model, or upgrading it, invalidates every stored vector — cosine
distance between two models' outputs is noise. That is why anything built on this
can never be a team default (decisions.md topic 6).
