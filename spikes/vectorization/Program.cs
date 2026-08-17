using System.Diagnostics;
using System.Globalization;
using Okf.Core.Search;
using Okf.Core.Vault;

namespace Okf.Spike.Vectorization;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine(
                """
                okf-spike-vec <command> [options]

                  smoke                       create a vec0 table, insert vectors, KNN query
                  index   [options]           embed every concept in a vault into a vec0 index
                  query   <text> [options]    KNN over that index
                  compare <text> [options]    lexical (okf search) vs vector, side by side

                options:
                  --vault <path>              vault/bundle root (default: discovery)
                  --db <path>                 index file (default: .okf/vectors.sqlite)
                  --embedder fake|onnx|http   default: fake
                  --endpoint <url>            http embedder endpoint
                  --model <name>              http/onnx model name
                  --onnx-dir <path>           directory holding model.onnx + vocab.txt
                  --limit <n>                 default 5
                  --keep                      incremental: re-embed only changed files
                """);
            return 2;
        }

        var options = Options.Parse(args);
        try
        {
            return args[0] switch
            {
                "smoke" => Smoke(options),
                "index" => Index(options),
                "query" => Query(options),
                "compare" => Compare(options),
                var other => Fail($"unknown command '{other}'"),
            };
        }
        catch (Exception exception)
        {
            for (Exception? current = exception; current is not null; current = current.InnerException)
            {
                Console.Error.WriteLine($"FAILED: {current.GetType().Name}: {current.Message}");
            }

            return 1;
        }
    }

    private static int Smoke(Options options)
    {
        var path = Path.Combine(Path.GetTempPath(), $"okf-spike-smoke-{Environment.ProcessId}.sqlite");
        using var store = VecStore.Open(path, dimensions: 4, reset: true);

        Console.WriteLine($"sqlite     {store.SqliteVersion()}");
        Console.WriteLine($"sqlite-vec {store.VecVersion()}");

        store.Put("b", "a.md", "unit x", "sha-a", [1f, 0f, 0f, 0f]);
        store.Put("b", "b.md", "unit y", "sha-b", [0f, 1f, 0f, 0f]);
        store.Put("b", "c.md", "diagonal", "sha-c", [0.7071f, 0.7071f, 0f, 0f]);

        foreach (var hit in store.Nearest([1f, 0f, 0f, 0f], k: 3))
        {
            Console.WriteLine(
                $"  {hit.Path,-8} distance={hit.Distance.ToString("F4", CultureInfo.InvariantCulture)} " +
                $"cosine={hit.Similarity.ToString("F4", CultureInfo.InvariantCulture)}");
        }

        File.Delete(path);
        Console.WriteLine("smoke OK");
        return 0;
    }

    private static int Index(Options options)
    {
        var items = Corpus.Load(options.Vault);
        using var embedder = options.CreateEmbedder();
        using var store = VecStore.Open(options.Database, embedder.Dimensions, reset: !options.Keep);

        var known = store.Fingerprints();
        var stopwatch = Stopwatch.StartNew();
        var embedded = 0;

        foreach (var item in items)
        {
            if (known.TryGetValue(item.BundleName + " " + item.RelativePath, out var sha) && sha == item.Sha256)
            {
                continue;
            }

            store.Put(item.BundleName, item.RelativePath, item.Title, item.Sha256, embedder.Embed(item.Text));
            embedded++;
        }

        stopwatch.Stop();
        Console.WriteLine(
            $"embedder   {embedder.Identity} ({embedder.Dimensions}d)\n" +
            $"concepts   {items.Count} ({embedded} embedded, {items.Count - embedded} unchanged)\n" +
            $"elapsed    {stopwatch.ElapsedMilliseconds} ms\n" +
            $"index      {options.Database} ({new FileInfo(options.Database).Length} bytes)");
        return 0;
    }

    private static int Query(Options options)
    {
        using var embedder = options.CreateEmbedder();
        using var store = VecStore.Open(options.Database, embedder.Dimensions);
        foreach (var hit in store.Nearest(embedder.Embed(options.Text), options.Limit))
        {
            Console.WriteLine(
                $"  {hit.Similarity.ToString("F4", CultureInfo.InvariantCulture)}  " +
                $"{hit.Bundle}/{hit.Path}  {hit.Title}");
        }

        return 0;
    }

    private static int Compare(Options options)
    {
        var environment = OkfEnvironment.FromProcess();
        var workingSet = OkfDiscovery.Resolve(options.Vault, environment);
        var outcome = OkfSearchEngine.Search(
            workingSet.Bundles,
            OkfSearchQuery.Parse(options.Text),
            new OkfSearchOptions { Limit = options.Limit });

        Console.WriteLine($"query: {options.Text}");
        Console.WriteLine($"lexical (okf search, BM25) — {outcome.TotalMatches} match(es), mode={outcome.MatchMode}");
        if (outcome.Results.Count == 0)
        {
            Console.WriteLine("  (nothing)");
        }

        foreach (var result in outcome.Results)
        {
            Console.WriteLine(
                $"  {result.Score.ToString("F4", CultureInfo.InvariantCulture)}  " +
                $"{result.BundleName}/{result.Path}  {result.Title}");
        }

        using var embedder = options.CreateEmbedder();
        using var store = VecStore.Open(options.Database, embedder.Dimensions);
        Console.WriteLine($"vector (sqlite-vec, cosine, {embedder.Identity})");
        foreach (var hit in store.Nearest(embedder.Embed(options.Text), options.Limit))
        {
            Console.WriteLine(
                $"  {hit.Similarity.ToString("F4", CultureInfo.InvariantCulture)}  " +
                $"{hit.Bundle}/{hit.Path}  {hit.Title}");
        }

        return 0;
    }

    private static int Fail(string message)
    {
        Console.Error.WriteLine(message);
        return 2;
    }
}

internal sealed class Options
{
    public string? Vault { get; private set; }

    public string Database { get; private set; } = Path.Combine(".okf", "vectors.sqlite");

    public string Embedder { get; private set; } = "fake";

    public string Endpoint { get; private set; } = "http://localhost:11434/v1/embeddings";

    public string Model { get; private set; } = "nomic-embed-text";

    public string OnnxDirectory { get; private set; } = "models/all-MiniLM-L6-v2";

    public int Limit { get; private set; } = 5;

    /// <summary>Keep the existing index and re-embed only what changed (sha256).</summary>
    public bool Keep { get; private set; }

    public string Text { get; private set; } = string.Empty;

    public static Options Parse(string[] args)
    {
        var options = new Options();
        var positional = new List<string>();

        for (var i = 1; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--vault": options.Vault = args[++i]; break;
                case "--db": options.Database = args[++i]; break;
                case "--embedder": options.Embedder = args[++i]; break;
                case "--endpoint": options.Endpoint = args[++i]; break;
                case "--model": options.Model = args[++i]; break;
                case "--onnx-dir": options.OnnxDirectory = args[++i]; break;
                case "--limit": options.Limit = int.Parse(args[++i], CultureInfo.InvariantCulture); break;
                case "--keep": options.Keep = true; break;
                default: positional.Add(args[i]); break;
            }
        }

        options.Text = string.Join(' ', positional);

        var directory = Path.GetDirectoryName(Path.GetFullPath(options.Database));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        return options;
    }

    public IEmbedder CreateEmbedder() => this.Embedder switch
    {
        "fake" => new FakeEmbedder(),
        "http" => new HttpEmbedder(this.Endpoint, this.Model, Environment.GetEnvironmentVariable("OKF_EMBED_API_KEY")),
#if SPIKE_ONNX
        "onnx" => new OnnxEmbedder(this.OnnxDirectory),
#else
        "onnx" => throw new InvalidOperationException(
            "rebuild with -p:SpikeOnnx=true to enable the ONNX embedder"),
#endif
        var other => throw new ArgumentException($"unknown embedder '{other}'"),
    };
}
