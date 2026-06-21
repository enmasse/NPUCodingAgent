namespace NPUCodingAgent.Embeddings;

/// <summary>
/// Utilities for inspecting and exporting decoder state for particle-filter experiments.
/// </summary>
public static class DecoderDiagnostics
{
    /// <summary>
    /// Export a complete decoder state snapshot including expansion layer and lookup layer.
    /// </summary>
    public sealed class FullDecoderSnapshot
    {
        public IReadOnlyList<float[]> ExpansionProjections { get; init; } = Array.Empty<float[]>();
        public DecoderSnapshot LookupSnapshot { get; init; }

        public FullDecoderSnapshot(
            IReadOnlyList<float[]> expansionProjections,
            DecoderSnapshot lookupSnapshot)
        {
            ArgumentNullException.ThrowIfNull(expansionProjections);
            ArgumentNullException.ThrowIfNull(lookupSnapshot);

            ExpansionProjections = expansionProjections;
            LookupSnapshot = lookupSnapshot;
        }
    }

    /// <summary>
    /// Export detailed per-step evidence for analysis.
    /// </summary>
    public sealed class DetailedStepEvidence
    {
        public int StepIndex { get; }
        public float[] SubEmbedding { get; }
        public DecoderStepResult Result { get; }

        public DetailedStepEvidence(int stepIndex, float[] subEmbedding, DecoderStepResult result)
        {
            ArgumentNullException.ThrowIfNull(subEmbedding);
            ArgumentNullException.ThrowIfNull(result);

            StepIndex = stepIndex;
            SubEmbedding = subEmbedding;
            Result = result;
        }
    }

    /// <summary>
    /// Compute diagnostic statistics for token vote evidence across a sequence.
    /// </summary>
    public sealed class VoteStatistics
    {
        public double AverageVoteCount { get; init; }
        public double AverageHarmonicScore { get; init; }
        public double AverageCosineSimilarity { get; init; }
        public int MinVoteCount { get; init; }
        public int MaxVoteCount { get; init; }

        public static VoteStatistics Compute(IEnumerable<DecoderStepResult> results)
        {
            ArgumentNullException.ThrowIfNull(results);

            var resultsList = results.ToList();
            if (resultsList.Count == 0)
            {
                throw new ArgumentException("Results collection cannot be empty.", nameof(results));
            }

            var voteCounts = resultsList.Select(r => r.Evidence.VoteCount).ToList();
            var harmonicScores = resultsList.Select(r => r.Evidence.HarmonicMeanScore).ToList();
            var cosines = resultsList
                .Where(r => r.Evidence.ExactCosineSimilarity.HasValue)
                .Select(r => r.Evidence.ExactCosineSimilarity!.Value)
                .ToList();

            return new VoteStatistics
            {
                AverageVoteCount = voteCounts.Average(),
                AverageHarmonicScore = harmonicScores.Average(),
                AverageCosineSimilarity = cosines.Count > 0 ? cosines.Average() : 0.0,
                MinVoteCount = voteCounts.Min(),
                MaxVoteCount = voteCounts.Max()
            };
        }
    }

    /// <summary>
    /// Export token prototype distribution for analysis.
    /// </summary>
    public static Dictionary<int, int> ComputeTokenUsageHistogram(IEnumerable<DecoderStepResult> results)
    {
        ArgumentNullException.ThrowIfNull(results);

        var histogram = new Dictionary<int, int>();

        foreach (var result in results)
        {
            int tokenId = result.Evidence.SelectedTokenId;
            if (!histogram.ContainsKey(tokenId))
            {
                histogram[tokenId] = 0;
            }
            histogram[tokenId]++;
        }

        return histogram;
    }

    /// <summary>
    /// Serialize projection parameters for external particle-filter tooling.
    /// </summary>
    public static string SerializeProjectionsToJson(IReadOnlyList<IReadOnlyList<float[]>> projections)
    {
        ArgumentNullException.ThrowIfNull(projections);

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("[");

        for (int tableIdx = 0; tableIdx < projections.Count; tableIdx++)
        {
            sb.AppendLine("  {");
            sb.AppendLine($"    \"tableIndex\": {tableIdx},");
            sb.AppendLine("    \"hyperplanes\": [");

            var hyperplanes = projections[tableIdx];
            for (int hpIdx = 0; hpIdx < hyperplanes.Count; hpIdx++)
            {
                sb.Append("      [");
                sb.Append(string.Join(", ", hyperplanes[hpIdx].Select(v => v.ToString("F6"))));
                sb.Append("]");
                if (hpIdx < hyperplanes.Count - 1)
                {
                    sb.AppendLine(",");
                }
                else
                {
                    sb.AppendLine();
                }
            }

            sb.Append("    ]");
            sb.AppendLine();
            sb.Append("  }");

            if (tableIdx < projections.Count - 1)
            {
                sb.AppendLine(",");
            }
            else
            {
                sb.AppendLine();
            }
        }

        sb.AppendLine("]");
        return sb.ToString();
    }
}
