using Betalgo.Ranul.OpenAI.ObjectModels.RequestModels;
using NPUCodingAgent.Services;
using Spectre.Console;

namespace NPUCodingAgent;

class Program
{
    static async Task Main(string[] args)
    {
        AnsiConsole.Write(
            new FigletText("NPU Coding Agent")
            {
                Justification = Justify.Left,
                Color = new Color(0, 255, 255)
            });
        AnsiConsole.WriteLine();

        AnsiConsole.MarkupLine("[dim]Version: MVP (v1.0)[/]");
        AnsiConsole.MarkupLine($"[dim]Workspace:[/] {Environment.CurrentDirectory}");
        AnsiConsole.MarkupLine($"[dim]Platform:[/]  {Environment.OSVersion.Platform} {Environment.OSVersion.Version}");
        AnsiConsole.WriteLine();

        var modelService = new LocalModelService();
        var workspaceService = new WorkspaceService(Environment.CurrentDirectory);
        var editService = new FileEditService();

        try
        {
            AnsiConsole.MarkupLine($"[cyan]Starting Foundry Local with selection '[yellow]{modelService.RequestedModelSelection}[/]'...[/]");
            try
            {
                await modelService.InitializeAsync();
                PrintRuntimeReady(modelService, await modelService.GetRuntimeInfoAsync());
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]✗ Error initializing Foundry Local: {ex.Message}[/]");
                AnsiConsole.WriteLine();
                await PrintSelectableModelsAsync(modelService, 20);
                PrintSetupGuide();
            }

            Console.WriteLine();
            PrintHelp();

            var chatHistory = new List<ChatMessage>
            {
                new("system", LocalModelService.SystemPrompt)
            };

            while (true)
            {
                Console.Write("\n> ");
                var input = Console.ReadLine();

                if (string.IsNullOrWhiteSpace(input))
                {
                    continue;
                }

                if (input.Equals("exit", StringComparison.OrdinalIgnoreCase)
                    || input.Equals("quit", StringComparison.OrdinalIgnoreCase))
                {
                    AnsiConsole.MarkupLine("\n[cyan]Goodbye![/]");
                    break;
                }

                if (input.Equals("help", StringComparison.OrdinalIgnoreCase))
                {
                    PrintHelp();
                    continue;
                }

                if (input.Equals("/list", StringComparison.OrdinalIgnoreCase))
                {
                    var files = workspaceService.ListFiles();
                    AnsiConsole.MarkupLine($"\n[cyan]Found {files.Count} files:[/]");
                    var table = new Table();
                    table.AddColumn("[cyan]File[/]");
                    
                    foreach (var file in files.Take(50))
                    {
                        table.AddRow(file);
                    }
                    
                    AnsiConsole.Write(table);

                    if (files.Count > 50)
                    {
                        AnsiConsole.MarkupLine($"[dim]... and {files.Count - 50} more[/]");
                    }

                    continue;
                }

                if (input.StartsWith("/read ", StringComparison.OrdinalIgnoreCase))
                {
                    var filePath = input.Substring(6).Trim();
                    var content = workspaceService.ReadFile(filePath);
                    if (content is not null)
                    {
                        var readPanel = new Panel(content)
                        {
                            Header = new PanelHeader($"[cyan]{filePath}[/]"),
                            Border = BoxBorder.Rounded
                        };
                        AnsiConsole.Write(readPanel);
                    }
                    else
                    {
                        AnsiConsole.MarkupLine($"[red]✗ Could not read file: {filePath}[/]");
                    }

                    continue;
                }

                if (input.Equals("/status", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        var status = string.Empty;
                        await AnsiConsole.Status()
                            .StartAsync("[cyan]Fetching model status...[/]", async ctx =>
                            {
                                status = await modelService.GetStatusAsync();
                            });
                        var statusPanel = new Panel(status)
                        {
                            Header = new PanelHeader("[cyan]Model Status[/]"),
                            Border = BoxBorder.Rounded,
                            Padding = new Padding(1, 1)
                        };
                        AnsiConsole.Write(statusPanel);
                    }
                    catch (Exception ex)
                    {
                        AnsiConsole.MarkupLine($"\n[red]✗ Status check failed: {ex.Message}[/]");
                    }

                    continue;
                }

                if (input.Equals("/ping", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        var pingResponse = string.Empty;
                        await AnsiConsole.Status()
                            .StartAsync("[cyan]Pinging model...[/]", async ctx =>
                            {
                                pingResponse = await modelService.PingAsync();
                            });
                        AnsiConsole.MarkupLine($"\n[green]✓ Ping response:[/] {pingResponse}");
                    }
                    catch (Exception ex)
                    {
                        AnsiConsole.MarkupLine($"\n[red]✗ Ping failed: {ex.Message}[/]");
                    }

                    continue;
                }

                if (input.Equals("/models", StringComparison.OrdinalIgnoreCase))
                {
                    await PrintSelectableModelsAsync(modelService);
                    continue;
                }

                if (input.StartsWith("/use ", StringComparison.OrdinalIgnoreCase))
                {
                    var modelSelection = input.Substring(5).Trim();
                    if (string.IsNullOrWhiteSpace(modelSelection))
                    {
                        AnsiConsole.MarkupLine("\n[yellow]Usage:[/] /use <model-alias-or-id>");
                        continue;
                    }

                    var nextModelService = new LocalModelService(modelSelection);
                    try
                    {
                        await AnsiConsole.Status()
                            .StartAsync($"[cyan]Switching to '[yellow]{modelSelection}[/]'...[/]", async ctx =>
                            {
                                await nextModelService.InitializeAsync();
                            });
                        var runtimeInfo = await nextModelService.GetRuntimeInfoAsync();
                        modelService.Dispose();
                        modelService = nextModelService;
                        chatHistory.Clear();
                        PrintRuntimeReady(modelService, runtimeInfo);
                    }
                    catch (Exception ex)
                    {
                        nextModelService.Dispose();
                        AnsiConsole.MarkupLine($"\n[red]✗ Model switch failed: {ex.Message}[/]");
                        await PrintSelectableModelsAsync(modelService, 20);
                    }

                    continue;
                }

                if (input.StartsWith("/edit ", StringComparison.OrdinalIgnoreCase))
                {
                    var filePath = input.Substring(6).Trim();
                    AnsiConsole.MarkupLine($"\n[cyan]Preparing to edit: {filePath}[/]");

                    var currentContent = workspaceService.ReadFile(filePath);
                    if (currentContent is null)
                    {
                        AnsiConsole.MarkupLine("[red]✗ File not found or not readable.[/]");
                        continue;
                    }

                    AnsiConsole.MarkupLine("[cyan]Describe the change you want:[/] ");
                    var changeRequest = Console.ReadLine();
                    if (string.IsNullOrWhiteSpace(changeRequest))
                    {
                        continue;
                    }

                    var context = $"Current file content:\n{currentContent}\n\nUser request: {changeRequest}\n\nProvide the complete updated file content.";
                    chatHistory.Add(new ChatMessage("user", context));

                    try
                    {
                        var response = string.Empty;
                        await AnsiConsole.Status()
                            .StartAsync("[cyan]Generating edits...[/]", async ctx =>
                            {
                                response = await modelService.GetResponseAsync(chatHistory);
                            });
                        chatHistory.Add(new ChatMessage("assistant", response));
                        PrintEditPreview(currentContent, response);

                        AnsiConsole.MarkupLine("\n[cyan]Type '[yellow]apply[/]' to save these changes, or anything else to cancel:[/] ");
                        var confirm = Console.ReadLine();
                        if (confirm?.Equals("apply", StringComparison.OrdinalIgnoreCase) == true)
                        {
                            if (editService.WriteFile(filePath, response))
                            {
                                AnsiConsole.MarkupLine("[green]✓ File updated successfully.[/]");
                            }
                            else
                            {
                                AnsiConsole.MarkupLine("[red]✗ Failed to write file.[/]");
                            }
                        }
                        else
                        {
                            AnsiConsole.MarkupLine("[yellow]Edit cancelled.[/]");
                        }
                    }
                    catch (Exception ex)
                    {
                        AnsiConsole.MarkupLine($"\n[red]✗ Edit request failed: {ex.Message}[/]");
                    }

                    continue;
                }

                chatHistory.Add(new ChatMessage("user", input));

                try
                {
                    string response = string.Empty;
                    await AnsiConsole.Status()
                        .StartAsync("[cyan]Thinking...[/]", async ctx =>
                        {
                            response = await modelService.GetResponseAsync(chatHistory);
                        });
                    chatHistory.Add(new ChatMessage("assistant", response));
                    AnsiConsole.MarkupLine($"\n[cyan]Assistant:[/]");
                    AnsiConsole.WriteLine(response);
                }
                catch (Exception ex)
                {
                    AnsiConsole.MarkupLine($"\n[red]✗ Error: {ex.Message}[/]");
                }

                if (chatHistory.Count > 20)
                {
                    chatHistory.RemoveRange(0, 4);
                }
            }
        }
        finally
        {
            modelService.Dispose();
        }
    }

    static void PrintRuntimeReady(LocalModelService modelService, LocalModelService.ModelRuntimeInfo runtimeInfo)
    {
        AnsiConsole.MarkupLine($"[green]✓[/] Foundry Local ready: [bold cyan]{modelService.ModelName}[/]");
        AnsiConsole.MarkupLine($"[dim]  Endpoint:[/] {runtimeInfo.Endpoint}");
        AnsiConsole.MarkupLine($"[dim]  Accelerator:[/] {runtimeInfo.AcceleratorSummary}");
        AnsiConsole.MarkupLine($"[dim]  NPU detected:[/] {(runtimeInfo.IsNpu ? "[green]yes[/]" : "no / unknown")}");
    }

    static async Task PrintSelectableModelsAsync(LocalModelService modelService, int? maxToShow = null)
    {
        try
        {
            IReadOnlyList<string> models = [];
            await AnsiConsole.Status()
                .StartAsync("[cyan]Loading available models...[/]", async ctx =>
                {
                    models = await modelService.ListAvailableModelsAsync();
                });

            if (models.Count == 0)
            {
                AnsiConsole.MarkupLine("\n[yellow]Foundry Local did not report any selectable models.[/]");
                return;
            }

            var table = new Table();
            table.Title = new TableTitle($"[cyan]Selectable Models ({models.Count})[/]");
            table.AddColumn("[cyan]Model[/]");
            
            var displayModels = maxToShow is int limit ? models.Take(limit) : models;
            foreach (var model in displayModels)
            {
                table.AddRow(model);
            }

            AnsiConsole.Write(table);

            if (maxToShow is int visibleCount && models.Count > visibleCount)
            {
                AnsiConsole.MarkupLine($"[dim]... and {models.Count - visibleCount} more[/]");
            }
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]✗ Model listing failed: {ex.Message}[/]");
        }
    }

    static void PrintHelp()
    {
        var table = new Table();
        table.Title = new TableTitle("[yellow]Available Commands[/]");
        table.AddColumn("[cyan]Command[/]");
        table.AddColumn("[dim]Description[/]");
        
        table.AddRow("[yellow]<message>[/]", "Chat with the coding assistant about your code");
        table.AddRow("[yellow]/list[/]", "List all source files in the workspace");
        table.AddRow("[yellow]/read <file>[/]", "Read and display a specific file");
        table.AddRow("[yellow]/status[/]", "Show the active model and local runtime details");
        table.AddRow("[yellow]/ping[/]", "Run a quick model smoke test");
        table.AddRow("[yellow]/models[/]", "List selectable Foundry Local aliases and variant IDs");
        table.AddRow("[yellow]/use <model>[/]", "Switch to a specific Foundry Local model alias or ID");
        table.AddRow("[yellow]/edit <file>[/]", "Edit a file with AI assistance (creates .backup)");
        table.AddRow("[yellow]help[/]", "Show this help message");
        table.AddRow("[yellow]exit[/]", "Exit the application");
        
        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();
        
        AnsiConsole.MarkupLine("[dim]Example workflow:[/]");
        AnsiConsole.MarkupLine("[dim]  > /list[/]");
        AnsiConsole.MarkupLine("[dim]  > /read Program.cs[/]");
        AnsiConsole.MarkupLine("[dim]  > /ping[/]");
        AnsiConsole.MarkupLine("[dim]  > /edit Program.cs[/]");
        AnsiConsole.MarkupLine("[dim]  > Add error handling to the Main method[/]");
    }

    static void PrintEditPreview(string currentContent, string proposedContent)
    {
        var previewLines = proposedContent
            .Split(Environment.NewLine)
            .Take(40)
            .ToList();

        var content = string.Join(Environment.NewLine, previewLines);
        var totalLines = proposedContent.Split(Environment.NewLine).Length;
        
        if (totalLines > previewLines.Count)
        {
            content += $"\n\n[dim]... ({totalLines - previewLines.Count} more lines)[/]";
        }

        var table = new Table();
        table.AddColumn("[cyan]Metric[/]");
        table.AddColumn("[cyan]Value[/]");
        table.AddRow("[dim]Current size[/]", $"{currentContent.Length} characters");
        table.AddRow("[dim]Proposed size[/]", $"{proposedContent.Length} characters");

        AnsiConsole.WriteLine();
        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();

        var previewPanel = new Panel(content)
        {
            Header = new PanelHeader("[cyan]Proposed Changes[/]"),
            Border = BoxBorder.Rounded
        };
        AnsiConsole.Write(previewPanel);
    }

    static void PrintSetupGuide()
    {
        var setupSteps = new List<string>
        {
            "[cyan]1.[/] Restore the project so the Foundry Local SDK packages are installed.",
            "[cyan]2.[/] Ensure Foundry Local can access a supported local model catalog.",
            "[cyan]3.[/] Optionally set the default model before running:",
            "     [dim]FOUNDRY_LOCAL_MODEL=phi-3-mini-128k-instruct-qnn-npu:4[/]",
            "[cyan]4.[/] Run [yellow]/models[/] to inspect every selectable alias and variant ID from the local catalog.",
            "[cyan]5.[/] Use [yellow]/use <model>[/] to switch to the exact model variant you want to run.",
            "[cyan]6.[/] Use [yellow]/status[/] to confirm the active model and whether Foundry Local exposed a web endpoint or is using the in-process SDK client."
        };

        var setupPanel = new Panel(string.Join("\n", setupSteps))
        {
            Header = new PanelHeader("[yellow]Setup Guide[/]"),
            Border = BoxBorder.Rounded,
            Padding = new Padding(1, 1)
        };
        
        AnsiConsole.Write(setupPanel);
        AnsiConsole.WriteLine();
        
        AnsiConsole.MarkupLine("[dim]For more information, visit:[/]");
        AnsiConsole.MarkupLine("[cyan]  https://learn.microsoft.com/en-us/azure/foundry-local/get-started[/]");
    }
}
