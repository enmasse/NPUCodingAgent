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

            var sentences = GetInputSentences(args);

            if (sentences.Count < 2)
            {
                AnsiConsole.MarkupLine("[red]Error: At least 2 sentences are required for comparison.[/]");
                return;
            }

            AnsiConsole.MarkupLine($"[cyan]Generating embeddings for {sentences.Count} inputs...[/]");

            IReadOnlyList<float[]> embeddings;
            await AnsiConsole.Status()
                .StartAsync("[cyan]Generating embeddings...[/]", async ctx =>
                {
                    embeddings = await modelService.GetEmbeddingsAsync(sentences);
                });

            embeddings = await modelService.GetEmbeddingsAsync(sentences);

            AnsiConsole.MarkupLine($"[green]✓[/] Generated {embeddings.Count} embeddings (dimension: {embeddings[0].Length})");
            Console.WriteLine();

            DisplayPairwiseComparisons(sentences, embeddings);
        }
        finally
        {
            modelService.Dispose();
        }
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
