using NPUCodingAgent.Embeddings;
using Xunit;

namespace NPUCodingAgent.Tests;

public sealed class VectorMathTests
{
    [Fact]
    public void CosineSimilarity_IdenticalVectors_ReturnsOne()
    {
        var vector = new float[] { 1.0f, 2.0f, 3.0f };

        var similarity = VectorMath.CosineSimilarity(vector, vector);

        Assert.Equal(1.0, similarity, precision: 6);
    }

    [Fact]
    public void CosineSimilarity_OrthogonalVectors_ReturnsZero()
    {
        var vector1 = new float[] { 1.0f, 0.0f, 0.0f };
        var vector2 = new float[] { 0.0f, 1.0f, 0.0f };

        var similarity = VectorMath.CosineSimilarity(vector1, vector2);

        Assert.Equal(0.0, similarity, precision: 6);
    }

    [Fact]
    public void CosineSimilarity_OppositeVectors_ReturnsNegativeOne()
    {
        var vector1 = new float[] { 1.0f, 2.0f, 3.0f };
        var vector2 = new float[] { -1.0f, -2.0f, -3.0f };

        var similarity = VectorMath.CosineSimilarity(vector1, vector2);

        Assert.Equal(-1.0, similarity, precision: 6);
    }

    [Fact]
    public void CosineSimilarity_MismatchedDimensions_ThrowsArgumentException()
    {
        var vector1 = new float[] { 1.0f, 2.0f };
        var vector2 = new float[] { 1.0f, 2.0f, 3.0f };

        var exception = Assert.Throws<ArgumentException>(() => VectorMath.CosineSimilarity(vector1, vector2));
        Assert.Contains("dimensions must match", exception.Message);
    }

    [Fact]
    public void CosineSimilarity_EmptyVectors_ThrowsArgumentException()
    {
        var vector1 = Array.Empty<float>();
        var vector2 = Array.Empty<float>();

        var exception = Assert.Throws<ArgumentException>(() => VectorMath.CosineSimilarity(vector1, vector2));
        Assert.Contains("cannot be empty", exception.Message);
    }

    [Fact]
    public void CosineSimilarity_NullVector_ThrowsArgumentNullException()
    {
        var vector = new float[] { 1.0f, 2.0f };

        Assert.Throws<ArgumentNullException>(() => VectorMath.CosineSimilarity(null!, vector));
        Assert.Throws<ArgumentNullException>(() => VectorMath.CosineSimilarity(vector, null!));
    }

    [Fact]
    public void CosineSimilarity_ZeroMagnitudeVector_ThrowsArgumentException()
    {
        var vector1 = new float[] { 0.0f, 0.0f, 0.0f };
        var vector2 = new float[] { 1.0f, 2.0f, 3.0f };

        var exception = Assert.Throws<ArgumentException>(() => VectorMath.CosineSimilarity(vector1, vector2));
        Assert.Contains("magnitude cannot be zero", exception.Message);
    }

    [Fact]
    public void VectorAngle_IdenticalVectors_ReturnsZero()
    {
        var vector = new float[] { 1.0f, 2.0f, 3.0f };

        var (radians, degrees) = VectorMath.VectorAngle(vector, vector);

        Assert.Equal(0.0, radians, precision: 6);
        Assert.Equal(0.0, degrees, precision: 2);
    }

    [Fact]
    public void VectorAngle_OrthogonalVectors_Returns90Degrees()
    {
        var vector1 = new float[] { 1.0f, 0.0f, 0.0f };
        var vector2 = new float[] { 0.0f, 1.0f, 0.0f };

        var (radians, degrees) = VectorMath.VectorAngle(vector1, vector2);

        Assert.Equal(Math.PI / 2.0, radians, precision: 6);
        Assert.Equal(90.0, degrees, precision: 2);
    }

    [Fact]
    public void VectorAngle_OppositeVectors_Returns180Degrees()
    {
        var vector1 = new float[] { 1.0f, 2.0f, 3.0f };
        var vector2 = new float[] { -1.0f, -2.0f, -3.0f };

        var (radians, degrees) = VectorMath.VectorAngle(vector1, vector2);

        Assert.Equal(Math.PI, radians, precision: 6);
        Assert.Equal(180.0, degrees, precision: 2);
    }

    [Fact]
    public void VectorAngle_ClampsBoundaryValues()
    {
        // Test with values that might produce cosine slightly outside [-1, 1] due to floating-point precision
        var vector1 = new float[] { 1.0f, 0.0f };
        var vector2 = new float[] { 1.0f, 0.0f };

        var (radians, degrees) = VectorMath.VectorAngle(vector1, vector2);

        Assert.InRange(radians, 0.0, Math.PI);
        Assert.InRange(degrees, 0.0, 180.0);
    }
}
