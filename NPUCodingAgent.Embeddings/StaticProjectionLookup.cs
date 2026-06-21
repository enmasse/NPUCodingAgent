namespace NPUCodingAgent.Embeddings;

/// <summary>
/// Static multi-table token lookup and voting layer.
/// Built once with fixed random projections, then queried many times for token selection.
/// </summary>
public sealed class StaticProjectionLookup
{
    private readonly RandomHyperplaneLshTable[] _tables;
    private readonly Dictionary<int, TokenPrototype> _tokenPrototypes;
    private readonly StaticTokenLookupOptions _options;

    public StaticProjectionLookup(StaticTokenLookupOptions options, IEnumerable<TokenPrototype> tokenPrototypes)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(tokenPrototypes);

        _options = options;
        _tokenPrototypes = tokenPrototypes.ToDictionary(t => t.Id);

        if (_tokenPrototypes.Count == 0)
        {
            throw new ArgumentException("At least one token prototype is required.", nameof(tokenPrototypes));
        }

        // Validate all token embeddings have the correct dimension
        foreach (var token in _tokenPrototypes.Values)
        {
            if (token.Embedding.Length != options.TokenEmbeddingDimension)
            {
                throw new ArgumentException(
                    $"Token {token.Id} has embedding dimension {token.Embedding.Length}, expected {options.TokenEmbeddingDimension}.",
                    nameof(tokenPrototypes));
            }
        }

        // Build independent LSH tables
        _tables = new RandomHyperplaneLshTable[options.TableCount];
        for (int i = 0; i < options.TableCount; i++)
        {
            int tableSeed = options.Seed + i;
            _tables[i] = new RandomHyperplaneLshTable(
                options.BitsPerTable,
                options.TokenEmbeddingDimension,
                tableSeed);

            // Index all tokens in this table
            foreach (var token in _tokenPrototypes.Values)
            {
                _tables[i].AddToken(token.Id, token.Embedding);
            }
        }
    }

    /// <summary>
    /// Decode a sub-embedding to a token using multi-table voting.
    /// </summary>
    public DecoderStepResult DecodeToken(float[] subEmbedding, bool computeExactCosine = false)
    {
        ArgumentNullException.ThrowIfNull(subEmbedding);

        if (subEmbedding.Length != _options.TokenEmbeddingDimension)
        {
            throw new ArgumentException(
                $"Sub-embedding dimension {subEmbedding.Length} does not match expected {_options.TokenEmbeddingDimension}.",
                nameof(subEmbedding));
        }

        // Collect candidates and scores from each table
        var candidateScores = new Dictionary<int, List<double>>();

        foreach (var table in _tables)
        {
            var candidates = table.GetCandidates(subEmbedding);

            // Take top-K candidates per table if configured
            var topCandidates = candidates.Take(_options.TopCandidatesPerTable);

            foreach (var tokenId in topCandidates)
            {
                if (!_tokenPrototypes.TryGetValue(tokenId, out var token))
                {
                    continue;
                }

                double localScore = table.ComputeLocalScore(
                    subEmbedding,
                    token.Embedding,
                    _options.ScoreEpsilon);

                if (!candidateScores.ContainsKey(tokenId))
                {
                    candidateScores[tokenId] = new List<double>();
                }

                candidateScores[tokenId].Add(localScore);
            }
        }

        // Fallback: if no hash collisions found, compute exact cosine with all tokens
        if (candidateScores.Count == 0)
        {
            foreach (var token in _tokenPrototypes.Values)
            {
                double cosine = VectorMath.CosineSimilarity(subEmbedding, token.Embedding);
                candidateScores[token.Id] = new List<double> { cosine };
            }
        }

        if (candidateScores.Count == 0)
        {
            throw new InvalidOperationException("No token candidates found. This indicates an empty token index.");
        }

        // Aggregate votes using harmonic mean
        var (selectedTokenId, voteCount, harmonicScore, perTableScores) =
            VoteAggregator.AggregateVotes(candidateScores, _options.ScoreEpsilon);

        // Optionally compute exact cosine similarity for diagnostics
        double? exactCosine = null;
        if (computeExactCosine && _tokenPrototypes.TryGetValue(selectedTokenId, out var selectedToken))
        {
            exactCosine = VectorMath.CosineSimilarity(subEmbedding, selectedToken.Embedding);
        }

        var evidence = new TokenVoteEvidence(
            selectedTokenId,
            voteCount,
            perTableScores,
            harmonicScore,
            exactCosine);

        var tokenText = _tokenPrototypes[selectedTokenId].Text;
        return new DecoderStepResult(tokenText, evidence);
    }

    /// <summary>
    /// Export a read-only snapshot of the projection parameters and token prototypes.
    /// </summary>
    public DecoderSnapshot ExportSnapshot()
    {
        var projectionParameters = _tables
            .Select(table => (IReadOnlyList<float[]>)table.Hyperplanes.ToArray())
            .ToArray();

        var tokenPrototypes = _tokenPrototypes.Values.ToArray();

        return new DecoderSnapshot(
            projectionParameters,
            tokenPrototypes,
            _options.TableCount,
            _options.BitsPerTable);
    }

    /// <summary>
    /// Get all token prototypes indexed in this lookup.
    /// </summary>
    public IReadOnlyCollection<TokenPrototype> GetTokenPrototypes() => _tokenPrototypes.Values;
}
