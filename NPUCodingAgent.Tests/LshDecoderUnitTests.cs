using NPUCodingAgent.Embeddings;
using Xunit;

namespace NPUCodingAgent.Tests;

public sealed class LshDecoderUnitTests
{
    [Fact]
    public void RandomHyperplaneLshTable_ProducesDeterministicSignatures()
    {
        const int dimension = 128;
        const int bitsPerTable = 12;
        const int seed = 42;

        var table1 = new RandomHyperplaneLshTable(bitsPerTable, dimension, seed);
        var table2 = new RandomHyperplaneLshTable(bitsPerTable, dimension, seed);

        var vector = new float[dimension];
        for (int i = 0; i < dimension; i++)
        {
            vector[i] = (float)(i / 100.0);
        }

        int sig1 = table1.ComputeSignature(vector);
        int sig2 = table2.ComputeSignature(vector);

        Assert.Equal(sig1, sig2);
    }

    [Fact]
    public void RandomHyperplaneLshTable_HandlesBucketCollisions()
    {
        const int dimension = 16;
        const int bitsPerTable = 4; // Small to force collisions
        const int seed = 42;

        var table = new RandomHyperplaneLshTable(bitsPerTable, dimension, seed);

        var vector1 = new float[dimension];
        var vector2 = new float[dimension];

        for (int i = 0; i < dimension; i++)
        {
            vector1[i] = 1.0f;
            vector2[i] = 1.1f;
        }

        table.AddToken(1, vector1);
        table.AddToken(2, vector2);

        var candidates1 = table.GetCandidates(vector1);
        Assert.Contains(1, candidates1);
    }

    [Fact]
    public void VoteAggregator_ComputesHarmonicMeanCorrectly()
    {
        var scores = new[] { 0.5, 0.6, 0.7 };
        double epsilon = 0.01;

        double harmonicMean = VoteAggregator.ComputeHarmonicMean(scores, epsilon);

        // Harmonic mean of 0.5, 0.6, 0.7 = 3 / (1/0.5 + 1/0.6 + 1/0.7) ≈ 0.5893
        Assert.InRange(harmonicMean, 0.58, 0.60);
    }

    [Fact]
    public void VoteAggregator_AggregatesVotesCorrectly()
    {
        var candidateScores = new Dictionary<int, List<double>>
        {
            { 1, new List<double> { 0.9, 0.8, 0.7 } },
            { 2, new List<double> { 0.6, 0.5 } },
            { 3, new List<double> { 0.95, 0.90, 0.85, 0.80 } }
        };

        var (tokenId, voteCount, harmonicScore, perTableScores) =
            VoteAggregator.AggregateVotes(candidateScores, 0.01);

        // Token 3 should win with 4 votes
        Assert.Equal(3, tokenId);
        Assert.Equal(4, voteCount);
        Assert.True(harmonicScore > 0);
        Assert.Equal(4, perTableScores.Count);
    }

    [Fact]
    public void SubEmbeddingSequence_RejectsEmptyEmbeddings()
    {
        Assert.Throws<ArgumentException>(() => new SubEmbeddingSequence(Array.Empty<float[]>()));
    }

    [Fact]
    public void LinearSequenceExpansionLayer_ProducesDeterministicSequence()
    {
        var options = new GlobalEmbeddingDecoderOptions
        {
            SubEmbeddingCount = 4,
            SubEmbeddingDimension = 64,
            Seed = 42
        };

        const int inputDim = 128;
        var layer1 = new LinearSequenceExpansionLayer(options, inputDim);
        var layer2 = new LinearSequenceExpansionLayer(options, inputDim);

        var input = new float[inputDim];
        for (int i = 0; i < inputDim; i++)
        {
            input[i] = (float)(i / 50.0);
        }

        var seq1 = layer1.Expand(input);
        var seq2 = layer2.Expand(input);

        Assert.Equal(seq1.Length, seq2.Length);

        for (int i = 0; i < seq1.Length; i++)
        {
            Assert.Equal(seq1.Embeddings[i].Length, seq2.Embeddings[i].Length);
            Assert.Equal(seq1.Embeddings[i], seq2.Embeddings[i]);
        }
    }

    [Fact]
    public void StaticProjectionLookup_SelectsTokenViaSingleVote()
    {
        var options = new StaticTokenLookupOptions
        {
            TableCount = 4,
            BitsPerTable = 8,
            TokenEmbeddingDimension = 32,
            Seed = 42,
            TopCandidatesPerTable = 2,
            ScoreEpsilon = 0.01
        };

        var tokens = new List<TokenPrototype>
        {
            new TokenPrototype(0, "hello", CreateVector(32, 0.5f)),
            new TokenPrototype(1, "world", CreateVector(32, 0.3f)),
            new TokenPrototype(2, "test", CreateVector(32, 0.8f))
        };

        var lookup = new StaticProjectionLookup(options, tokens);

        var queryEmbedding = CreateVector(32, 0.49f);
        var result = lookup.DecodeToken(queryEmbedding, computeExactCosine: true);

        Assert.NotNull(result);
        Assert.NotEmpty(result.TokenText);
        Assert.True(result.Evidence.VoteCount > 0);
        Assert.True(result.Evidence.HarmonicMeanScore > 0);
        Assert.NotNull(result.Evidence.ExactCosineSimilarity);
    }

    [Fact]
    public void StaticProjectionLookup_ExportsSnapshot()
    {
        var options = new StaticTokenLookupOptions
        {
            TableCount = 16,
            BitsPerTable = 12,
            TokenEmbeddingDimension = 128,
            Seed = 42
        };

        var tokens = new List<TokenPrototype>
        {
            new TokenPrototype(0, "token1", CreateVector(128, 0.1f)),
            new TokenPrototype(1, "token2", CreateVector(128, 0.2f))
        };

        var lookup = new StaticProjectionLookup(options, tokens);
        var snapshot = lookup.ExportSnapshot();

        Assert.Equal(16, snapshot.TableCount);
        Assert.Equal(12, snapshot.BitsPerTable);
        Assert.Equal(16, snapshot.ProjectionParameters.Count);
        Assert.Equal(2, snapshot.TokenPrototypes.Count);

        // Verify each table has correct number of hyperplanes
        foreach (var tableProjections in snapshot.ProjectionParameters)
        {
            Assert.Equal(12, tableProjections.Count);
            foreach (var hyperplane in tableProjections)
            {
                Assert.Equal(128, hyperplane.Length);
            }
        }
    }

    [Fact]
    public void TokenPrototype_RejectsInvalidInputs()
    {
        Assert.Throws<ArgumentException>(() => new TokenPrototype(0, "", new float[10]));
        Assert.Throws<ArgumentNullException>(() => new TokenPrototype(0, "test", null!));
        Assert.Throws<ArgumentException>(() => new TokenPrototype(0, "test", Array.Empty<float>()));
    }

    [Fact]
    public void DecoderDiagnostics_ComputesVoteStatistics()
    {
        var results = new List<DecoderStepResult>
        {
            new DecoderStepResult("token1", new TokenVoteEvidence(0, 5, new[] { 0.9, 0.8 }, 0.85, 0.95)),
            new DecoderStepResult("token2", new TokenVoteEvidence(1, 3, new[] { 0.7 }, 0.70, 0.80)),
            new DecoderStepResult("token3", new TokenVoteEvidence(2, 7, new[] { 0.85, 0.90 }, 0.87, 0.92))
        };

        var stats = DecoderDiagnostics.VoteStatistics.Compute(results);

        Assert.Equal(5.0, stats.AverageVoteCount);
        Assert.InRange(stats.AverageHarmonicScore, 0.8, 0.9);
        Assert.InRange(stats.AverageCosineSimilarity, 0.88, 0.90);
        Assert.Equal(3, stats.MinVoteCount);
        Assert.Equal(7, stats.MaxVoteCount);
    }

    private static float[] CreateVector(int dimension, float baseValue)
    {
        var vector = new float[dimension];
        for (int i = 0; i < dimension; i++)
        {
            vector[i] = baseValue + (i * 0.01f);
        }
        return vector;
    }
}
