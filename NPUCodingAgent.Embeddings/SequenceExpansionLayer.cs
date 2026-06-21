namespace NPUCodingAgent.Embeddings;

/// <summary>
/// Sequence expansion layer that maps one input embedding to a configurable sequence of smaller embeddings.
/// This contract leaves room for later particle-filter training without committing to one implementation now.
/// </summary>
public interface ISequenceExpansionLayer
{
    /// <summary>
    /// Expand one global embedding into a sequence of smaller embeddings.
    /// </summary>
    SubEmbeddingSequence Expand(float[] globalEmbedding);
}

/// <summary>
/// Simple deterministic sequence-expansion layer for initial implementation.
/// Uses learned or random linear projections to emit sub-embeddings.
/// Later can be replaced with trained/particle-filter-based expansion.
/// </summary>
public sealed class LinearSequenceExpansionLayer : ISequenceExpansionLayer
{
    private readonly float[][] _projectionMatrices;
    private readonly int _subEmbeddingCount;
    private readonly int _subEmbeddingDimension;
    private readonly int _inputDimension;

    public LinearSequenceExpansionLayer(GlobalEmbeddingDecoderOptions options, int inputDimension)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (inputDimension <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(inputDimension), "Input dimension must be positive.");
        }

        _subEmbeddingCount = options.SubEmbeddingCount;
        _subEmbeddingDimension = options.SubEmbeddingDimension;
        _inputDimension = inputDimension;

        // Initialize deterministic random projection matrices
        var random = new Random(options.Seed);
        _projectionMatrices = new float[_subEmbeddingCount][];

        for (int i = 0; i < _subEmbeddingCount; i++)
        {
            _projectionMatrices[i] = GenerateProjectionMatrix(inputDimension, _subEmbeddingDimension, random);
        }
    }

    private static float[] GenerateProjectionMatrix(int inputDim, int outputDim, Random random)
    {
        // Flatten matrix: inputDim x outputDim stored as 1D array
        var matrix = new float[inputDim * outputDim];

        for (int i = 0; i < inputDim * outputDim; i++)
        {
            // Xavier/Glorot-style initialization
            double u1 = random.NextDouble();
            double u2 = random.NextDouble();
            double gaussian = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
            double scale = Math.Sqrt(2.0 / (inputDim + outputDim));
            matrix[i] = (float)(gaussian * scale);
        }

        return matrix;
    }

    public SubEmbeddingSequence Expand(float[] globalEmbedding)
    {
        ArgumentNullException.ThrowIfNull(globalEmbedding);

        if (globalEmbedding.Length != _inputDimension)
        {
            throw new ArgumentException(
                $"Global embedding dimension {globalEmbedding.Length} does not match expected {_inputDimension}.",
                nameof(globalEmbedding));
        }

        var subEmbeddings = new float[_subEmbeddingCount][];

        for (int i = 0; i < _subEmbeddingCount; i++)
        {
            subEmbeddings[i] = ProjectToSubEmbedding(globalEmbedding, _projectionMatrices[i]);
        }

        return new SubEmbeddingSequence(subEmbeddings);
    }

    private float[] ProjectToSubEmbedding(float[] input, float[] projectionMatrix)
    {
        var output = new float[_subEmbeddingDimension];

        for (int outIdx = 0; outIdx < _subEmbeddingDimension; outIdx++)
        {
            float sum = 0f;
            for (int inIdx = 0; inIdx < _inputDimension; inIdx++)
            {
                sum += input[inIdx] * projectionMatrix[inIdx * _subEmbeddingDimension + outIdx];
            }
            output[outIdx] = sum;
        }

        // Apply L2 normalization
        float norm = 0f;
        for (int i = 0; i < _subEmbeddingDimension; i++)
        {
            norm += output[i] * output[i];
        }

        norm = (float)Math.Sqrt(norm);
        if (norm > 1e-8f)
        {
            for (int i = 0; i < _subEmbeddingDimension; i++)
            {
                output[i] /= norm;
            }
        }

        return output;
    }

    /// <summary>
    /// Export projection matrices for inspection or particle-filter use.
    /// </summary>
    public IReadOnlyList<float[]> ExportProjectionMatrices() => _projectionMatrices;
}
