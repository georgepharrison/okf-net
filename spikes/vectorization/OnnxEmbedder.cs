#if SPIKE_ONNX
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Microsoft.ML.Tokenizers;

namespace Okf.Spike.Vectorization;

/// <summary>
/// Option (a): a local sentence-transformer run through ONNX Runtime. The model is a
/// sidecar on disk (<c>model.onnx</c> + <c>vocab.txt</c>), never embedded — 90 MB does
/// not belong in a 5 MB CLI. Mean-pooled over the attention mask, then L2-normalised,
/// which is what sentence-transformers does for all-MiniLM-L6-v2.
/// </summary>
internal sealed class OnnxEmbedder : IEmbedder
{
    private const int MaxTokens = 256;

    private readonly InferenceSession session;
    private readonly BertTokenizer tokenizer;
    private readonly string identity;

    public OnnxEmbedder(string directory)
    {
        var model = Path.Combine(directory, "model.onnx");
        var vocabulary = Path.Combine(directory, "vocab.txt");
        this.session = new InferenceSession(model);
        this.tokenizer = BertTokenizer.Create(vocabulary);
        this.identity = $"onnx:{Path.GetFileName(Path.TrimEndingDirectorySeparator(directory))}";
    }

    public int Dimensions => 384;

    public string Identity => this.identity;

    public float[] Embed(string text)
    {
        var ids = this.tokenizer.EncodeToIds(text, MaxTokens, out _, out _);
        var length = ids.Count;

        var inputIds = new DenseTensor<long>([1, length]);
        var attention = new DenseTensor<long>([1, length]);
        var types = new DenseTensor<long>([1, length]);
        for (var i = 0; i < length; i++)
        {
            inputIds[0, i] = ids[i];
            attention[0, i] = 1;
            types[0, i] = 0;
        }

        using var results = this.session.Run(
        [
            NamedOnnxValue.CreateFromTensor("input_ids", inputIds),
            NamedOnnxValue.CreateFromTensor("attention_mask", attention),
            NamedOnnxValue.CreateFromTensor("token_type_ids", types),
        ]);

        var hidden = results.First().AsTensor<float>();
        var pooled = new float[Dimensions];
        for (var token = 0; token < length; token++)
        {
            for (var dimension = 0; dimension < Dimensions; dimension++)
            {
                pooled[dimension] += hidden[0, token, dimension];
            }
        }

        for (var dimension = 0; dimension < Dimensions; dimension++)
        {
            pooled[dimension] /= length;
        }

        return FakeEmbedder.Normalize(pooled);
    }

    public void Dispose() => this.session.Dispose();
}
#endif
