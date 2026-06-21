using Betalgo.Ranul.OpenAI.ObjectModels.RequestModels;
using NPUCodingAgent.Embeddings;
using NPUCodingAgent.Services;
using Xunit;

namespace NPUCodingAgent.Tests;

public sealed class LshDecoderIntegrationTests
{
    [Fact]
    public async Task HierarchicalDecoder_WorksEndToEndWithRealEmbeddings()
    {
        // This test requires Foundry Local to be available
        await using var embeddingService = new LocalModelService(isEmbeddingModel: true);
        await embeddingService.InitializeAsync();

        // Build a small token vocabulary
        var words = new[] { "hello", "world", "test", "example", "coding", "agent" };
        var wordEmbeddings = await embeddingService.GetEmbeddingsAsync(words);

        var tokenPrototypes = new List<TokenPrototype>();
        for (int i = 0; i < words.Length; i++)
        {
            tokenPrototypes.Add(new TokenPrototype(i, words[i], wordEmbeddings[i]));
        }

        // Generate a global embedding from input
        var inputSentences = new[] { "hello world coding example" };
        var globalEmbeddings = await embeddingService.GetEmbeddingsAsync(inputSentences);
        var globalEmbedding = globalEmbeddings[0];

        // Configure decoder
        var decoderOptions = new GlobalEmbeddingDecoderOptions
        {
            SubEmbeddingCount = 4,
            SubEmbeddingDimension = globalEmbedding.Length,
            Seed = 42
        };

        var lookupOptions = new StaticTokenLookupOptions
        {
            TableCount = 16,
            BitsPerTable = 12,
            TokenEmbeddingDimension = globalEmbedding.Length,
            Seed = 42,
            TopCandidatesPerTable = 3,
            ScoreEpsilon = 0.01
        };

        // Initialize decoder components
        var expansionLayer = new LinearSequenceExpansionLayer(decoderOptions, globalEmbedding.Length);
        var tokenLookup = new StaticProjectionLookup(lookupOptions, tokenPrototypes);

        // Expand to sub-embeddings
        var subEmbeddings = expansionLayer.Expand(globalEmbedding);
        Assert.Equal(4, subEmbeddings.Length);
        Assert.Equal(globalEmbedding.Length, subEmbeddings.Dimension);

        // Decode each sub-embedding
        var decodedTokens = new List<DecoderStepResult>();
        foreach (var subEmbedding in subEmbeddings.Embeddings)
        {
            var result = tokenLookup.DecodeToken(subEmbedding, computeExactCosine: true);
            decodedTokens.Add(result);
        }

        Assert.Equal(4, decodedTokens.Count);

        // Verify all tokens were successfully decoded
        foreach (var result in decodedTokens)
        {
            Assert.NotEmpty(result.TokenText);
            Assert.True(result.Evidence.VoteCount > 0);
            Assert.True(result.Evidence.HarmonicMeanScore > 0);
            Assert.NotNull(result.Evidence.ExactCosineSimilarity);
            Assert.InRange(result.Evidence.ExactCosineSimilarity.Value, -1.0, 1.0);
        }

        // Verify snapshot export works
        var snapshot = tokenLookup.ExportSnapshot();
        Assert.Equal(16, snapshot.TableCount);
        Assert.Equal(12, snapshot.BitsPerTable);
        Assert.Equal(tokenPrototypes.Count, snapshot.TokenPrototypes.Count);
    }

    [Fact]
    public async Task StaticProjectionLookup_ProducesDeterministicResultsWithRealEmbeddings()
    {
        await using var embeddingService = new LocalModelService(isEmbeddingModel: true);
        await embeddingService.InitializeAsync();

        // Build vocabulary
        var words = new[] { "apple", "banana", "orange", "grape", "melon" };
        var wordEmbeddings = await embeddingService.GetEmbeddingsAsync(words);

        var tokenPrototypes = wordEmbeddings.Select((emb, idx) =>
            new TokenPrototype(idx, words[idx], emb)).ToList();

        var options = new StaticTokenLookupOptions
        {
            TableCount = 16,
            BitsPerTable = 12,
            TokenEmbeddingDimension = wordEmbeddings[0].Length,
            Seed = 42,
            TopCandidatesPerTable = 2,
            ScoreEpsilon = 0.01
        };

        // Create two instances with same seed
        var lookup1 = new StaticProjectionLookup(options, tokenPrototypes);
        var lookup2 = new StaticProjectionLookup(options, tokenPrototypes);

        // Query both with same embedding
        var queryEmbeddings = await embeddingService.GetEmbeddingsAsync(new[] { "apple" });
        var queryEmbedding = queryEmbeddings[0];

        var result1 = lookup1.DecodeToken(queryEmbedding, computeExactCosine: true);
        var result2 = lookup2.DecodeToken(queryEmbedding, computeExactCosine: true);

        // Results should be identical
        Assert.Equal(result1.TokenText, result2.TokenText);
        Assert.Equal(result1.Evidence.SelectedTokenId, result2.Evidence.SelectedTokenId);
        Assert.Equal(result1.Evidence.VoteCount, result2.Evidence.VoteCount);
        Assert.Equal(result1.Evidence.HarmonicMeanScore, result2.Evidence.HarmonicMeanScore);
    }

    [Fact]
    public async Task LinearSequenceExpansionLayer_PreservesEmbeddingStructure()
    {
        await using var embeddingService = new LocalModelService(isEmbeddingModel: true);
        await embeddingService.InitializeAsync();

        var sentences = new[] { "The quick brown fox jumps over the lazy dog" };
        var embeddings = await embeddingService.GetEmbeddingsAsync(sentences);
        var globalEmbedding = embeddings[0];

        var options = new GlobalEmbeddingDecoderOptions
        {
            SubEmbeddingCount = 8,
            SubEmbeddingDimension = globalEmbedding.Length,
            Seed = 42
        };

        var expansionLayer = new LinearSequenceExpansionLayer(options, globalEmbedding.Length);
        var subEmbeddings = expansionLayer.Expand(globalEmbedding);

        Assert.Equal(8, subEmbeddings.Length);

        // Verify all sub-embeddings are normalized
        foreach (var subEmb in subEmbeddings.Embeddings)
        {
            double norm = 0.0;
            for (int i = 0; i < subEmb.Length; i++)
            {
                norm += subEmb[i] * subEmb[i];
            }
            norm = Math.Sqrt(norm);

            // Should be approximately normalized (L2 norm ≈ 1)
            Assert.InRange(norm, 0.99, 1.01);
        }
    }

    [Fact]
    public async Task DecoderDiagnostics_CapturesCompleteStateSnapshot()
    {
        await using var embeddingService = new LocalModelService(isEmbeddingModel: true);
        await embeddingService.InitializeAsync();

        var words = new[] { "one", "two", "three", "four", "five" };
        var wordEmbeddings = await embeddingService.GetEmbeddingsAsync(words);

        var tokenPrototypes = wordEmbeddings.Select((emb, idx) =>
            new TokenPrototype(idx, words[idx], emb)).ToList();

        var globalEmbeddings = await embeddingService.GetEmbeddingsAsync(new[] { "test sentence" });
        var globalEmbedding = globalEmbeddings[0];

        var decoderOptions = new GlobalEmbeddingDecoderOptions
        {
            SubEmbeddingCount = 3,
            SubEmbeddingDimension = globalEmbedding.Length,
            Seed = 123
        };

        var lookupOptions = new StaticTokenLookupOptions
        {
            TableCount = 16,
            BitsPerTable = 10,
            TokenEmbeddingDimension = globalEmbedding.Length,
            Seed = 456
        };

        var expansionLayer = new LinearSequenceExpansionLayer(decoderOptions, globalEmbedding.Length);
        var tokenLookup = new StaticProjectionLookup(lookupOptions, tokenPrototypes);

        var subEmbeddings = expansionLayer.Expand(globalEmbedding);
        var decodedTokens = subEmbeddings.Embeddings
            .Select(emb => tokenLookup.DecodeToken(emb, computeExactCosine: true))
            .ToList();

        // Export snapshots
        var lookupSnapshot = tokenLookup.ExportSnapshot();
        var expansionProjections = expansionLayer.ExportProjectionMatrices();

        // Verify snapshot completeness
        Assert.NotNull(lookupSnapshot);
        Assert.Equal(16, lookupSnapshot.TableCount);
        Assert.Equal(10, lookupSnapshot.BitsPerTable);
        Assert.Equal(tokenPrototypes.Count, lookupSnapshot.TokenPrototypes.Count);

        Assert.NotNull(expansionProjections);
        Assert.Equal(3, expansionProjections.Count);

        // Verify diagnostics work
        var stats = DecoderDiagnostics.VoteStatistics.Compute(decodedTokens);
        Assert.True(stats.AverageVoteCount > 0);
        Assert.True(stats.AverageHarmonicScore > 0);

        var histogram = DecoderDiagnostics.ComputeTokenUsageHistogram(decodedTokens);
        Assert.NotEmpty(histogram);
    }
}
