using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Okf.Core.Search;

namespace Okf.Spike.Vectorization;

/// <summary>The one seam a production integration would need.</summary>
internal interface IEmbedder : IDisposable
{
    /// <summary>Vector width. Fixed per embedder — it is baked into the vec0 schema.</summary>
    int Dimensions { get; }

    /// <summary>
    /// The embedder's identity, recorded next to the index. Changing it must invalidate
    /// every stored vector: cosine distances between two models' outputs are noise.
    /// </summary>
    string Identity { get; }

    /// <summary>Embeds one text, L2-normalised.</summary>
    float[] Embed(string text);
}

/// <summary>
/// A DETERMINISTIC FAKE. Hashes tokens into a fixed-width space (the classic hashing
/// trick) and L2-normalises. It proves the storage/query plumbing end to end with no
/// model and no network, and it is honest about what it is NOT: hashed tokens have no
/// shared subspace, so "K8s" and "Kubernetes" land orthogonally. It can never
/// demonstrate the vocabulary-mismatch rescue that motivates the spike.
/// </summary>
internal sealed class FakeEmbedder : IEmbedder
{
    public int Dimensions => 384;

    public string Identity => "fake-hash-384-v1";

    public float[] Embed(string text)
    {
        var vector = new float[Dimensions];
        foreach (var token in OkfTokenizer.Tokenize(text))
        {
            var digest = SHA256.HashData(Encoding.UTF8.GetBytes(token));
            var slot = (int)(BitConverter.ToUInt32(digest, 0) % (uint)Dimensions);
            var sign = (digest[4] & 1) == 0 ? 1f : -1f;
            vector[slot] += sign;
        }

        return Normalize(vector);
    }

    public void Dispose()
    {
    }

    internal static float[] Normalize(float[] vector)
    {
        var norm = 0.0;
        foreach (var value in vector)
        {
            norm += value * value;
        }

        norm = Math.Sqrt(norm);
        if (norm <= 0)
        {
            return vector;
        }

        for (var i = 0; i < vector.Length; i++)
        {
            vector[i] = (float)(vector[i] / norm);
        }

        return vector;
    }
}

/// <summary>
/// Option (b): HTTP POST to an OpenAI-compatible <c>/v1/embeddings</c>. No SDK, no
/// dependency — <c>HttpClient</c> and one source-generated JSON contract, which is why
/// this option costs the AOT story nothing.
/// </summary>
internal sealed class HttpEmbedder : IEmbedder
{
    private readonly HttpClient client = new() { Timeout = TimeSpan.FromSeconds(30) };
    private readonly string endpoint;
    private readonly string model;
    private int dimensions;

    public HttpEmbedder(string endpoint, string model, string? apiKey)
    {
        this.endpoint = endpoint;
        this.model = model;
        if (!string.IsNullOrEmpty(apiKey))
        {
            this.client.DefaultRequestHeaders.Authorization = new("Bearer", apiKey);
        }
    }

    public int Dimensions => this.dimensions is 0 ? this.dimensions = Embed("probe").Length : this.dimensions;

    public string Identity => $"http:{this.model}";

    public float[] Embed(string text)
    {
        using var response = this.client.PostAsJsonAsync(
            this.endpoint,
            new EmbeddingRequest(this.model, text),
            EmbeddingJson.Default.EmbeddingRequest).GetAwaiter().GetResult();
        response.EnsureSuccessStatusCode();

        var payload = response.Content
            .ReadFromJsonAsync(EmbeddingJson.Default.EmbeddingResponse)
            .GetAwaiter().GetResult()
            ?? throw new InvalidOperationException("empty embeddings response");

        return FakeEmbedder.Normalize(payload.Data[0].Embedding);
    }

    public void Dispose() => this.client.Dispose();
}

internal sealed record EmbeddingRequest(
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("input")] string Input);

internal sealed record EmbeddingResponse(
    [property: JsonPropertyName("data")] List<EmbeddingDatum> Data);

internal sealed record EmbeddingDatum(
    [property: JsonPropertyName("embedding")] float[] Embedding);

/// <summary>Source-generated, so the HTTP option stays reflection-free under AOT.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(EmbeddingRequest))]
[JsonSerializable(typeof(EmbeddingResponse))]
internal sealed partial class EmbeddingJson : JsonSerializerContext;
