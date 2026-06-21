namespace NPUCodingAgent.Embeddings;

public static class VectorMath
{
    public static double CosineSimilarity(float[] vector1, float[] vector2)
    {
        if (vector1 is null || vector2 is null)
        {
            throw new ArgumentNullException(vector1 is null ? nameof(vector1) : nameof(vector2));
        }

        if (vector1.Length != vector2.Length)
        {
            throw new ArgumentException($"Vector dimensions must match. Got {vector1.Length} and {vector2.Length}.");
        }

        if (vector1.Length == 0)
        {
            throw new ArgumentException("Vectors cannot be empty.", nameof(vector1));
        }

        double dotProduct = 0.0;
        double magnitude1 = 0.0;
        double magnitude2 = 0.0;

        for (int i = 0; i < vector1.Length; i++)
        {
            dotProduct += vector1[i] * vector2[i];
            magnitude1 += vector1[i] * vector1[i];
            magnitude2 += vector2[i] * vector2[i];
        }

        magnitude1 = Math.Sqrt(magnitude1);
        magnitude2 = Math.Sqrt(magnitude2);

        if (magnitude1 == 0.0 || magnitude2 == 0.0)
        {
            throw new ArgumentException("Vector magnitude cannot be zero.");
        }

        return dotProduct / (magnitude1 * magnitude2);
    }

    public static (double Radians, double Degrees) VectorAngle(float[] vector1, float[] vector2)
    {
        var cosineSimilarity = CosineSimilarity(vector1, vector2);

        // Clamp to [-1, 1] to handle floating-point precision issues
        cosineSimilarity = Math.Clamp(cosineSimilarity, -1.0, 1.0);

        var radians = Math.Acos(cosineSimilarity);
        var degrees = radians * (180.0 / Math.PI);

        return (radians, degrees);
    }
}
