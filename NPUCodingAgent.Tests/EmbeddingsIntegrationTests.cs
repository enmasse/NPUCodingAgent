using NPUCodingAgent.Services;
using NPUCodingAgent.Embeddings;
using Xunit;

namespace NPUCodingAgent.Tests;

public sealed class EmbeddingsIntegrationTests
{
    [Fact]
    public async Task GetEmbeddingsAsync_WithMultipleSentences_ReturnsMatchingVectors()
    {
        await using var service = new LocalModelService(isEmbeddingModel: true);
        await service.InitializeAsync();

        var sentences = new List<string>
        {
            "The quick brown fox jumps over the lazy dog.",
            "A fast auburn canine leaps above an idle hound."
        };

        var embeddings = await service.GetEmbeddingsAsync(sentences);

        Assert.Equal(2, embeddings.Count);
        Assert.NotEmpty(embeddings[0]);
        Assert.NotEmpty(embeddings[1]);
        Assert.Equal(embeddings[0].Length, embeddings[1].Length);
    }

    [Fact]
    public async Task GetEmbeddingsAsync_ComputesSimilarityAndAngle_ForRelatedSentences()
    {
        await using var service = new LocalModelService(isEmbeddingModel: true);
        await service.InitializeAsync();

        var sentences = new List<string>
        {
            "The cat sits on the mat.",
            "A feline rests on the rug.",
            "The weather is sunny today."
        };

        var embeddings = await service.GetEmbeddingsAsync(sentences);

        var similarity01 = VectorMath.CosineSimilarity(embeddings[0], embeddings[1]);
        var similarity02 = VectorMath.CosineSimilarity(embeddings[0], embeddings[2]);

        // Related sentences should have higher similarity than unrelated ones
        Assert.True(similarity01 > similarity02, 
            $"Expected similarity between related sentences ({similarity01:F6}) to be higher than unrelated ({similarity02:F6})");

        var (radians01, degrees01) = VectorMath.VectorAngle(embeddings[0], embeddings[1]);
        var (radians02, degrees02) = VectorMath.VectorAngle(embeddings[0], embeddings[2]);

        // Related sentences should have smaller angle than unrelated ones
        Assert.True(degrees01 < degrees02,
            $"Expected angle between related sentences ({degrees01:F2}°) to be smaller than unrelated ({degrees02:F2}°)");

        // Angles should be in valid range
        Assert.InRange(radians01, 0.0, Math.PI);
        Assert.InRange(degrees01, 0.0, 180.0);
    }

    [Fact]
    public async Task GetEmbeddingsAsync_EmptyTextList_ThrowsArgumentException()
    {
        await using var service = new LocalModelService(isEmbeddingModel: true);
        await service.InitializeAsync();

        var exception = await Assert.ThrowsAsync<ArgumentException>(() => 
            service.GetEmbeddingsAsync(new List<string>()));

        Assert.Contains("At least one text is required", exception.Message);
    }

    [Fact]
    public async Task GetEmbeddingsAsync_WithWhitespaceText_ThrowsArgumentException()
    {
        await using var service = new LocalModelService(isEmbeddingModel: true);
        await service.InitializeAsync();

        var sentences = new List<string> { "Valid sentence", "   " };

        var exception = await Assert.ThrowsAsync<ArgumentException>(() => 
            service.GetEmbeddingsAsync(sentences));

        Assert.Contains("cannot be null or whitespace", exception.Message);
    }
}
