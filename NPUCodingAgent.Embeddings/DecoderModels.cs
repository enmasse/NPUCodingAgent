namespace NPUCodingAgent.Embeddings;

/// <summary>
/// Options for the global embedding decoder that expands one input embedding into a sequence of smaller embeddings.
/// </summary>
public sealed record GlobalEmbeddingDecoderOptions
{
    /// <summary>
    /// Number of sub-embeddings to emit from one input embedding.
    /// </summary>
    public int SubEmbeddingCount { get; init; } = 8;

    /// <summary>
    /// Dimension of each sub-embedding.
    /// </summary>
    public int SubEmbeddingDimension { get; init; } = 128;

    /// <summary>
    /// Seed for deterministic random initialization when needed.
    /// </summary>
    public int Seed { get; init; } = 42;

    /// <summary>
    /// Maximum CPU-bound candidates to track during decoding.
    /// </summary>
    public int MaxCandidates { get; init; } = 128;
}

/// <summary>
/// Read-only sequence of smaller embeddings emitted from one input embedding.
/// </summary>
public sealed class SubEmbeddingSequence
{
    public IReadOnlyList<float[]> Embeddings { get; }

    public SubEmbeddingSequence(IReadOnlyList<float[]> embeddings)
    {
        ArgumentNullException.ThrowIfNull(embeddings);
        if (embeddings.Count == 0)
        {
            throw new ArgumentException("Sub-embedding sequence cannot be empty.", nameof(embeddings));
        }

        Embeddings = embeddings;
    }

    public int Length => Embeddings.Count;

    public int Dimension => Embeddings[0].Length;
}

/// <summary>
/// Options for the static token lookup layer that uses random projections and multi-table voting.
/// </summary>
public sealed record StaticTokenLookupOptions
{
    /// <summary>
    /// Number of independent hash tables for token lookup.
    /// </summary>
    public int TableCount { get; init; } = 16;

    /// <summary>
    /// Number of hash bits per table.
    /// </summary>
    public int BitsPerTable { get; init; } = 12;

    /// <summary>
    /// Dimension of token prototype embeddings.
    /// </summary>
    public int TokenEmbeddingDimension { get; init; } = 128;

    /// <summary>
    /// Seed for deterministic random projection generation.
    /// </summary>
    public int Seed { get; init; } = 42;

    /// <summary>
    /// Maximum number of top candidates to retrieve per table during lookup.
    /// </summary>
    public int TopCandidatesPerTable { get; init; } = 3;

    /// <summary>
    /// Minimum epsilon value for scoring to avoid zero-division in harmonic mean calculations.
    /// </summary>
    public double ScoreEpsilon { get; init; } = 0.01;
}

/// <summary>
/// A token prototype with its text representation and embedding vector.
/// </summary>
public sealed class TokenPrototype
{
    public int Id { get; }
    public string Text { get; }
    public float[] Embedding { get; }

    public TokenPrototype(int id, string text, float[] embedding)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ArgumentException("Token text cannot be null or whitespace.", nameof(text));
        }

        ArgumentNullException.ThrowIfNull(embedding);
        if (embedding.Length == 0)
        {
            throw new ArgumentException("Token embedding cannot be empty.", nameof(embedding));
        }

        Id = id;
        Text = text;
        Embedding = embedding;
    }
}

/// <summary>
/// Evidence captured during token voting across multiple LSH tables.
/// </summary>
public sealed class TokenVoteEvidence
{
    /// <summary>
    /// The selected token ID.
    /// </summary>
    public int SelectedTokenId { get; }

    /// <summary>
    /// Total vote count for the selected token.
    /// </summary>
    public int VoteCount { get; }

    /// <summary>
    /// Per-table scores for the selected token (for diagnostics and tie-breaking).
    /// </summary>
    public IReadOnlyList<double> PerTableScores { get; }

    /// <summary>
    /// Harmonic mean of per-table scores (tie-break metric).
    /// </summary>
    public double HarmonicMeanScore { get; }

    /// <summary>
    /// Optional exact cosine similarity between query sub-embedding and selected token prototype.
    /// </summary>
    public double? ExactCosineSimilarity { get; }

    public TokenVoteEvidence(
        int selectedTokenId,
        int voteCount,
        IReadOnlyList<double> perTableScores,
        double harmonicMeanScore,
        double? exactCosineSimilarity = null)
    {
        ArgumentNullException.ThrowIfNull(perTableScores);

        SelectedTokenId = selectedTokenId;
        VoteCount = voteCount;
        PerTableScores = perTableScores;
        HarmonicMeanScore = harmonicMeanScore;
        ExactCosineSimilarity = exactCosineSimilarity;
    }
}

/// <summary>
/// Result of decoding one sub-embedding to a token.
/// </summary>
public sealed class DecoderStepResult
{
    /// <summary>
    /// The selected token text.
    /// </summary>
    public string TokenText { get; }

    /// <summary>
    /// Vote and scoring evidence for this decoding step.
    /// </summary>
    public TokenVoteEvidence Evidence { get; }

    public DecoderStepResult(string tokenText, TokenVoteEvidence evidence)
    {
        if (string.IsNullOrWhiteSpace(tokenText))
        {
            throw new ArgumentException("Token text cannot be null or whitespace.", nameof(tokenText));
        }

        ArgumentNullException.ThrowIfNull(evidence);

        TokenText = tokenText;
        Evidence = evidence;
    }
}

/// <summary>
/// Read-only snapshot of decoder state for later particle-filter experiments or analysis.
/// </summary>
public sealed class DecoderSnapshot
{
    /// <summary>
    /// Random projection parameters (hyperplanes) for each table.
    /// Each entry is a table, containing hyperplane vectors.
    /// </summary>
    public IReadOnlyList<IReadOnlyList<float[]>> ProjectionParameters { get; }

    /// <summary>
    /// All token prototypes indexed by the static lookup layer.
    /// </summary>
    public IReadOnlyList<TokenPrototype> TokenPrototypes { get; }

    /// <summary>
    /// Number of tables used.
    /// </summary>
    public int TableCount { get; }

    /// <summary>
    /// Bits per table used.
    /// </summary>
    public int BitsPerTable { get; }

    public DecoderSnapshot(
        IReadOnlyList<IReadOnlyList<float[]>> projectionParameters,
        IReadOnlyList<TokenPrototype> tokenPrototypes,
        int tableCount,
        int bitsPerTable)
    {
        ArgumentNullException.ThrowIfNull(projectionParameters);
        ArgumentNullException.ThrowIfNull(tokenPrototypes);

        ProjectionParameters = projectionParameters;
        TokenPrototypes = tokenPrototypes;
        TableCount = tableCount;
        BitsPerTable = bitsPerTable;
    }
}
