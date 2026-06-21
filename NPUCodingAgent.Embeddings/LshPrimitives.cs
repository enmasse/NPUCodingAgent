using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("NPUCodingAgent.Tests")]

namespace NPUCodingAgent.Embeddings;

/// <summary>
/// CPU-efficient random hyperplane LSH table for cosine-style lookup.
/// Uses deterministic seeded projections and bit-packed signatures for fast bucket retrieval.
/// </summary>
internal sealed class RandomHyperplaneLshTable
{
    private readonly float[][] _hyperplanes;
    private readonly Dictionary<int, List<int>> _buckets;
    private readonly int _bitsPerTable;
    private readonly int _dimension;

    public IReadOnlyList<float[]> Hyperplanes => _hyperplanes;

    public RandomHyperplaneLshTable(int bitsPerTable, int dimension, int seed)
    {
        if (bitsPerTable <= 0 || bitsPerTable > 24)
        {
            throw new ArgumentOutOfRangeException(nameof(bitsPerTable), "Bits per table must be between 1 and 24.");
        }

        if (dimension <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(dimension), "Dimension must be positive.");
        }

        _bitsPerTable = bitsPerTable;
        _dimension = dimension;
        _buckets = new Dictionary<int, List<int>>();

        // Generate deterministic random hyperplanes
        var random = new Random(seed);
        _hyperplanes = new float[bitsPerTable][];
        for (int i = 0; i < bitsPerTable; i++)
        {
            _hyperplanes[i] = GenerateRandomHyperplane(dimension, random);
        }
    }

    private static float[] GenerateRandomHyperplane(int dimension, Random random)
    {
        var hyperplane = new float[dimension];
        double sumSquared = 0.0;

        // Generate random Gaussian-like values
        for (int i = 0; i < dimension; i++)
        {
            // Box-Muller transform for pseudo-Gaussian
            double u1 = random.NextDouble();
            double u2 = random.NextDouble();
            double value = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
            hyperplane[i] = (float)value;
            sumSquared += value * value;
        }

        // Normalize
        float norm = (float)Math.Sqrt(sumSquared);
        if (norm > 0)
        {
            for (int i = 0; i < dimension; i++)
            {
                hyperplane[i] /= norm;
            }
        }

        return hyperplane;
    }

    /// <summary>
    /// Compute the hash signature for a vector.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int ComputeSignature(float[] vector)
    {
        if (vector.Length != _dimension)
        {
            throw new ArgumentException($"Vector dimension mismatch. Expected {_dimension}, got {vector.Length}.", nameof(vector));
        }

        int signature = 0;
        for (int bit = 0; bit < _bitsPerTable; bit++)
        {
            float dotProduct = ComputeDotProduct(vector, _hyperplanes[bit]);
            if (dotProduct >= 0)
            {
                signature |= (1 << bit);
            }
        }

        return signature;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float ComputeDotProduct(float[] a, float[] b)
    {
        float sum = 0f;
        for (int i = 0; i < a.Length; i++)
        {
            sum += a[i] * b[i];
        }
        return sum;
    }

    /// <summary>
    /// Add a token prototype to the table.
    /// </summary>
    public void AddToken(int tokenId, float[] embedding)
    {
        int signature = ComputeSignature(embedding);
        if (!_buckets.TryGetValue(signature, out var bucket))
        {
            bucket = new List<int>();
            _buckets[signature] = bucket;
        }

        bucket.Add(tokenId);
    }

    /// <summary>
    /// Retrieve token candidates from the bucket matching the query embedding.
    /// </summary>
    public IReadOnlyList<int> GetCandidates(float[] queryEmbedding)
    {
        int signature = ComputeSignature(queryEmbedding);
        if (_buckets.TryGetValue(signature, out var bucket))
        {
            return bucket;
        }

        return Array.Empty<int>();
    }

    /// <summary>
    /// Compute a local match score between query signature and token signature.
    /// Returns a value between 0 and 1 based on Hamming similarity.
    /// </summary>
    public double ComputeLocalScore(int querySignature, int tokenSignature, double epsilon)
    {
        int xorResult = querySignature ^ tokenSignature;
        int hammingDistance = CountSetBits(xorResult);
        int maxDistance = _bitsPerTable;

        // Convert Hamming distance to similarity: 0 distance = 1.0, max distance = epsilon
        double similarity = 1.0 - ((double)hammingDistance / maxDistance);
        return Math.Max(similarity, epsilon);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int CountSetBits(int value)
    {
        int count = 0;
        while (value != 0)
        {
            count++;
            value &= value - 1;
        }
        return count;
    }

    /// <summary>
    /// Compute local score for query embedding against a token embedding.
    /// </summary>
    public double ComputeLocalScore(float[] queryEmbedding, float[] tokenEmbedding, double epsilon)
    {
        int querySignature = ComputeSignature(queryEmbedding);
        int tokenSignature = ComputeSignature(tokenEmbedding);
        return ComputeLocalScore(querySignature, tokenSignature, epsilon);
    }
}

/// <summary>
/// Vote aggregator using harmonic mean for combining per-table scores.
/// </summary>
internal static class VoteAggregator
{
    /// <summary>
    /// Compute harmonic mean of scores, with epsilon floor to avoid zero-division.
    /// </summary>
    public static double ComputeHarmonicMean(IEnumerable<double> scores, double epsilon)
    {
        double sumReciprocals = 0.0;
        int count = 0;

        foreach (var score in scores)
        {
            double clampedScore = Math.Max(score, epsilon);
            sumReciprocals += 1.0 / clampedScore;
            count++;
        }

        if (count == 0)
        {
            return 0.0;
        }

        return count / sumReciprocals;
    }

    /// <summary>
    /// Aggregate votes across tables and return top candidate by vote count, with harmonic-mean tie-breaking.
    /// </summary>
    public static (int tokenId, int voteCount, double harmonicScore, IReadOnlyList<double> perTableScores) AggregateVotes(
        Dictionary<int, List<double>> candidateScores,
        double epsilon)
    {
        if (candidateScores.Count == 0)
        {
            throw new InvalidOperationException("No candidates to aggregate.");
        }

        var ranked = candidateScores
            .Select(kvp => new
            {
                TokenId = kvp.Key,
                VoteCount = kvp.Value.Count,
                HarmonicScore = ComputeHarmonicMean(kvp.Value, epsilon),
                PerTableScores = kvp.Value.AsReadOnly()
            })
            .OrderByDescending(x => x.VoteCount)
            .ThenByDescending(x => x.HarmonicScore)
            .First();

        return (ranked.TokenId, ranked.VoteCount, ranked.HarmonicScore, ranked.PerTableScores);
    }
}
