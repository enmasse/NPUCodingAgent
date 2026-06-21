using NPUCodingAgent.Services;
using Spectre.Console;

namespace NPUCodingAgent.Embeddings;

class Program
{
    static async Task Main(string[] args)
    {
        AnsiConsole.Write(
            new FigletText("NPU Embeddings")
            {
                Justification = Justify.Left,
                Color = new Color(0, 255, 255)
            });
        AnsiConsole.WriteLine();

        AnsiConsole.MarkupLine("[dim]Version: MVP (v1.0)[/]");
        AnsiConsole.MarkupLine($"[dim]Workspace:[/] {Environment.CurrentDirectory}");
        AnsiConsole.WriteLine();

        var modelSelection = Environment.GetEnvironmentVariable("FOUNDRY_LOCAL_EMBEDDING_MODEL");
        var modelService = new LocalModelService(modelSelection, isEmbeddingModel: true);

        try
        {
            AnsiConsole.MarkupLine($"[cyan]Starting Foundry Local with embedding model \u0027[yellow]{modelService.RequestedModelSelection}[/]\u0027...[/]");

            try
            {
                await modelService.InitializeAsync();
                var runtimeInfo = await modelService.GetRuntimeInfoAsync();
                PrintRuntimeReady(modelService, runtimeInfo);
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]✗ Error initializing Foundry Local: {ex.Message}[/]");
                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine("[yellow]Hint:[/] Set FOUNDRY_LOCAL_EMBEDDING_MODEL environment variable to override the default model.");
                return;
            }

            Console.WriteLine();

            // Ask user which workflow to run
            var workflow = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("Select workflow:")
                    .AddChoices(
                        "Pairwise Embedding Comparison",
                        "Hierarchical Decoder (LSH Token Voting)"));

            Console.WriteLine();

            if (workflow == "Pairwise Embedding Comparison")
            {
                await RunPairwiseComparisonWorkflow(modelService);
            }
            else
            {
                await RunHierarchicalDecoderWorkflow(modelService);
            }
        }
        finally
        {
            modelService.Dispose();
        }
    }

    static async Task RunPairwiseComparisonWorkflow(LocalModelService modelService)
    {
        var sentences = GetInputSentences(Array.Empty<string>());

        if (sentences.Count < 2)
        {
            AnsiConsole.MarkupLine("[red]Error: At least 2 sentences are required for comparison.[/]");
            return;
        }

        AnsiConsole.MarkupLine($"[cyan]Generating embeddings for {sentences.Count} inputs...[/]");

        var embeddings = await modelService.GetEmbeddingsAsync(sentences);

        AnsiConsole.MarkupLine($"[green]✓[/] Generated {embeddings.Count} embeddings (dimension: {embeddings[0].Length})");
        Console.WriteLine();

        DisplayPairwiseComparisons(sentences, embeddings);
    }

    static async Task RunHierarchicalDecoderWorkflow(LocalModelService modelService)
    {
        AnsiConsole.MarkupLine("[cyan]Hierarchical Decoder with LSH Token Voting[/]");
        Console.WriteLine();

        // Step 1: Get or generate a global embedding
        AnsiConsole.MarkupLine("[cyan]Enter a sentence to generate a global embedding:[/]");
        Console.Write("> ");
        var inputSentence = Console.ReadLine();

        if (string.IsNullOrWhiteSpace(inputSentence))
        {
            AnsiConsole.MarkupLine("[red]No input provided.[/]");
            return;
        }

        var globalEmbeddings = await modelService.GetEmbeddingsAsync(new[] { inputSentence });
        var globalEmbedding = globalEmbeddings[0];

        AnsiConsole.MarkupLine($"[green]✓[/] Generated global embedding (dimension: {globalEmbedding.Length})");
        Console.WriteLine();

        // Step 2: Build token vocabulary with embeddings
        AnsiConsole.MarkupLine("[cyan]Building token vocabulary...[/]");
        var tokenVocabulary = await BuildTokenVocabulary(modelService);
        AnsiConsole.MarkupLine($"[green]✓[/] Built vocabulary with {tokenVocabulary.Count} tokens");
        Console.WriteLine();

        // Step 3: Initialize decoder components
        var decoderOptions = new GlobalEmbeddingDecoderOptions
        {
            SubEmbeddingCount = 8,
            SubEmbeddingDimension = globalEmbedding.Length, // Match global embedding dimension for now
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

        AnsiConsole.MarkupLine("[cyan]Initializing sequence expansion layer...[/]");
        var expansionLayer = new LinearSequenceExpansionLayer(decoderOptions, globalEmbedding.Length);

        AnsiConsole.MarkupLine("[cyan]Initializing static token lookup layer (16 tables)...[/]");
        var tokenLookup = new StaticProjectionLookup(lookupOptions, tokenVocabulary);

        AnsiConsole.MarkupLine($"[green]✓[/] Decoder initialized");
        Console.WriteLine();

        // Step 4: Expand to sub-embeddings
        AnsiConsole.MarkupLine("[cyan]Expanding to sub-embedding sequence...[/]");
        var subEmbeddings = expansionLayer.Expand(globalEmbedding);
        AnsiConsole.MarkupLine($"[green]✓[/] Generated {subEmbeddings.Length} sub-embeddings (dimension: {subEmbeddings.Dimension})");
        Console.WriteLine();

        // Step 5: Decode each sub-embedding to tokens
        AnsiConsole.MarkupLine("[cyan]Decoding sub-embeddings to tokens via multi-table voting...[/]");
        var decodedTokens = new List<DecoderStepResult>();

        foreach (var subEmbedding in subEmbeddings.Embeddings)
        {
            var result = tokenLookup.DecodeToken(subEmbedding, computeExactCosine: true);
            decodedTokens.Add(result);
        }

        AnsiConsole.MarkupLine($"[green]✓[/] Decoded {decodedTokens.Count} tokens");
        Console.WriteLine();

        // Step 6: Display results
        DisplayDecoderResults(inputSentence, decodedTokens);

        // Step 7: Optionally export snapshot for particle-filter experiments
        var snapshot = tokenLookup.ExportSnapshot();
        AnsiConsole.MarkupLine($"[dim]Decoder snapshot available: {snapshot.TableCount} tables, {snapshot.BitsPerTable} bits/table, {snapshot.TokenPrototypes.Count} tokens[/]");
    }

    static async Task<List<TokenPrototype>> BuildTokenVocabulary(LocalModelService modelService)
    {
        // Build a small demonstration vocabulary
        // In a real system, this would come from a corpus or training data
        var words = new[]
        {
            "the", "a", "an", "and", "or", "but", "if", "then",
            "is", "are", "was", "were", "be", "been", "being",
            "have", "has", "had", "do", "does", "did",
            "will", "would", "could", "should", "may", "might",
            "can", "must", "shall",
            "I", "you", "he", "she", "it", "we", "they",
            "this", "that", "these", "those",
            "in", "on", "at", "by", "for", "with", "from", "to",
            "of", "about", "over", "under", "above", "below",
            "hello", "world", "test", "example", "sample", "demo",
            "code", "program", "function", "method", "class", "object",
            "data", "value", "string", "number", "boolean", "array",
            "item", "element", "node", "tree", "list", "map",
            "good", "bad", "big", "small", "new", "old", "fast", "slow"
        };

        var embeddings = await modelService.GetEmbeddingsAsync(words);

        var tokenPrototypes = new List<TokenPrototype>();
        for (int i = 0; i < words.Length; i++)
        {
            tokenPrototypes.Add(new TokenPrototype(i, words[i], embeddings[i]));
        }

        return tokenPrototypes;
    }

    static void DisplayDecoderResults(string inputSentence, List<DecoderStepResult> decodedTokens)
    {
        var table = new Table();
        table.Title = new TableTitle("[cyan]Decoded Token Sequence[/]");
        table.AddColumn("[cyan]Step[/]");
        table.AddColumn("[cyan]Token[/]");
        table.AddColumn("[cyan]Votes[/]");
        table.AddColumn("[cyan]Harmonic Score[/]");
        table.AddColumn("[cyan]Cosine Similarity[/]");

        for (int i = 0; i < decodedTokens.Count; i++)
        {
            var result = decodedTokens[i];
            table.AddRow(
                $"{i + 1}",
                $"[yellow]{result.TokenText}[/]",
                $"{result.Evidence.VoteCount}",
                $"{result.Evidence.HarmonicMeanScore:F4}",
                result.Evidence.ExactCosineSimilarity.HasValue
                    ? $"{result.Evidence.ExactCosineSimilarity.Value:F4}"
                    : "N/A"
            );
        }

        AnsiConsole.Write(table);
        Console.WriteLine();

        var assembledText = string.Join(" ", decodedTokens.Select(t => t.TokenText));
        var textPanel = new Panel(assembledText)
        {
            Header = new PanelHeader("[cyan]Reconstructed Text[/]"),
            Border = BoxBorder.Rounded
        };
        AnsiConsole.Write(textPanel);
        Console.WriteLine();

        var inputPanel = new Panel(inputSentence)
        {
            Header = new PanelHeader("[cyan]Original Input[/]"),
            Border = BoxBorder.Rounded
        };
        AnsiConsole.Write(inputPanel);
    }

    static List<string> GetInputSentences(string[] args)
    {
        var sentences = new List<string>();

        if (args.Length >= 2)
        {
            sentences.AddRange(args);
            AnsiConsole.MarkupLine($"[dim]Using {args.Length} sentences from command-line arguments[/]");
        }
        else
        {
            AnsiConsole.MarkupLine("[cyan]Enter sentences to compare (minimum 2, empty line to finish):[/]");
            Console.WriteLine();

            int count = 1;
            while (true)
            {
                Console.Write($"{count}> ");
                var input = Console.ReadLine();

                if (string.IsNullOrWhiteSpace(input))
                {
                    if (sentences.Count >= 2)
                    {
                        break;
                    }
                    else if (sentences.Count == 0)
                    {
                        AnsiConsole.MarkupLine("[yellow]Please enter at least 2 sentences.[/]");
                        continue;
                    }
                    else
                    {
                        AnsiConsole.MarkupLine("[yellow]Please enter at least 1 more sentence.[/]");
                        continue;
                    }
                }

                sentences.Add(input.Trim());
                count++;
            }
        }

        Console.WriteLine();
        return sentences;
    }

    static void DisplayPairwiseComparisons(List<string> sentences, IReadOnlyList<float[]> embeddings)
    {
        var table = new Table();
        table.Title = new TableTitle("[cyan]Pairwise Embedding Comparisons[/]");
        table.AddColumn("[cyan]Pair[/]");
        table.AddColumn("[cyan]Cosine Similarity[/]");
        table.AddColumn("[cyan]Angle (Radians)[/]");
        table.AddColumn("[cyan]Angle (Degrees)[/]");

        for (int i = 0; i < embeddings.Count; i++)
        {
            for (int j = i + 1; j < embeddings.Count; j++)
            {
                var similarity = VectorMath.CosineSimilarity(embeddings[i], embeddings[j]);
                var (radians, degrees) = VectorMath.VectorAngle(embeddings[i], embeddings[j]);

                var sentence1 = sentences[i].Length > 40 ? sentences[i].Substring(0, 37) + "..." : sentences[i];
                var sentence2 = sentences[j].Length > 40 ? sentences[j].Substring(0, 37) + "..." : sentences[j];

                table.AddRow(
                    $"[dim]{i + 1}[/] ↔ [dim]{j + 1}[/]",
                    $"{similarity:F6}",
                    $"{radians:F6}",
                    $"{degrees:F2}°"
                );
            }
        }

        AnsiConsole.Write(table);
        Console.WriteLine();

        var sentencePanel = new Panel(string.Join("\n", sentences.Select((s, i) => $"[cyan]{i + 1}.[/] {s}")))
        {
            Header = new PanelHeader("[cyan]Input Sentences[/]"),
            Border = BoxBorder.Rounded
        };
        AnsiConsole.Write(sentencePanel);
    }

    static void PrintRuntimeReady(LocalModelService modelService, LocalModelService.ModelRuntimeInfo runtimeInfo)
    {
        AnsiConsole.MarkupLine($"[green]✓[/] Foundry Local ready: [bold cyan]{modelService.ModelName}[/]");
        AnsiConsole.MarkupLine($"[dim]  Endpoint:[/] {runtimeInfo.Endpoint}");
        AnsiConsole.MarkupLine($"[dim]  Accelerator:[/] {runtimeInfo.AcceleratorSummary}");
        AnsiConsole.MarkupLine($"[dim]  NPU detected:[/] {(runtimeInfo.IsNpu ? "[green]yes[/]" : "no / unknown")}");
    }
}
