using NPUCodingAgent.Services;

namespace NPUCodingAgent;

class Program
{
    static async Task Main(string[] args)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("╔════════════════════════════════════════╗");
        Console.WriteLine("║     NPU Coding Agent MVP (v1.0)        ║");
        Console.WriteLine("╚════════════════════════════════════════╝");
        Console.ResetColor();
        Console.WriteLine();
        Console.WriteLine($"Workspace: {Environment.CurrentDirectory}");
        Console.WriteLine($"Platform:  {Environment.OSVersion.Platform} {Environment.OSVersion.Version}");
        Console.WriteLine();

        var modelService = new LocalModelService();
        var workspaceService = new WorkspaceService(Environment.CurrentDirectory);
        var editService = new FileEditService();

        try
        {
            Console.WriteLine($"Starting Foundry Local with selection '{modelService.RequestedModelSelection}'...");
            try
            {
                await modelService.InitializeAsync();
                PrintRuntimeReady(modelService, await modelService.GetRuntimeInfoAsync());
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"✗ Error initializing Foundry Local: {ex.Message}");
                Console.ResetColor();
                Console.WriteLine();
                await PrintSelectableModelsAsync(modelService, 20);
                PrintSetupGuide();
            }

            Console.WriteLine();
            PrintHelp();

            var chatHistory = new List<(string role, string content)>();

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
                    Console.WriteLine("Goodbye!");
                    break;
                }

                if (input.Equals("help", StringComparison.OrdinalIgnoreCase))
                {
                    PrintHelp();
                    continue;
                }

                if (input.StartsWith("/list", StringComparison.OrdinalIgnoreCase))
                {
                    var files = workspaceService.ListFiles();
                    Console.WriteLine($"\nFound {files.Count} files:");
                    foreach (var file in files.Take(50))
                    {
                        Console.WriteLine($"  {file}");
                    }

                    if (files.Count > 50)
                    {
                        Console.WriteLine($"  ... and {files.Count - 50} more");
                    }

                    continue;
                }

                if (input.StartsWith("/read ", StringComparison.OrdinalIgnoreCase))
                {
                    var filePath = input.Substring(6).Trim();
                    var content = workspaceService.ReadFile(filePath);
                    if (content is not null)
                    {
                        Console.WriteLine($"\n--- {filePath} ---");
                        Console.WriteLine(content);
                        Console.WriteLine("--- End ---");
                    }
                    else
                    {
                        Console.WriteLine($"Could not read file: {filePath}");
                    }

                    continue;
                }

                if (input.Equals("/status", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        var status = await modelService.GetStatusAsync();
                        Console.WriteLine($"\n{status}");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"\nStatus check failed: {ex.Message}");
                    }

                    continue;
                }

                if (input.Equals("/ping", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        var pingResponse = await modelService.PingAsync();
                        Console.WriteLine($"\nPing response: {pingResponse}");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"\nPing failed: {ex.Message}");
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
                        Console.WriteLine("\nUsage: /use <model-alias-or-id>");
                        continue;
                    }

                    var nextModelService = new LocalModelService(modelSelection);
                    try
                    {
                        Console.WriteLine($"\nSwitching to '{modelSelection}'...");
                        await nextModelService.InitializeAsync();
                        var runtimeInfo = await nextModelService.GetRuntimeInfoAsync();
                        modelService.Dispose();
                        modelService = nextModelService;
                        chatHistory.Clear();
                        PrintRuntimeReady(modelService, runtimeInfo);
                    }
                    catch (Exception ex)
                    {
                        nextModelService.Dispose();
                        Console.WriteLine($"\nModel switch failed: {ex.Message}");
                        await PrintSelectableModelsAsync(modelService, 20);
                    }

                    continue;
                }

                if (input.StartsWith("/edit ", StringComparison.OrdinalIgnoreCase))
                {
                    var filePath = input.Substring(6).Trim();
                    Console.WriteLine($"\nPreparing to edit: {filePath}");

                    var currentContent = workspaceService.ReadFile(filePath);
                    if (currentContent is null)
                    {
                        Console.WriteLine("File not found or not readable.");
                        continue;
                    }

                    Console.Write("Describe the change you want: ");
                    var changeRequest = Console.ReadLine();
                    if (string.IsNullOrWhiteSpace(changeRequest))
                    {
                        continue;
                    }

                    var context = $"Current file content:\n{currentContent}\n\nUser request: {changeRequest}\n\nProvide the complete updated file content.";
                    chatHistory.Add(("user", context));

                    try
                    {
                        var response = await modelService.GetResponseAsync(chatHistory);
                        chatHistory.Add(("assistant", response));
                        PrintEditPreview(currentContent, response);

                        Console.Write("\nType 'apply' to save these changes, or anything else to cancel: ");
                        var confirm = Console.ReadLine();
                        if (confirm?.Equals("apply", StringComparison.OrdinalIgnoreCase) == true)
                        {
                            if (editService.WriteFile(filePath, response))
                            {
                                Console.WriteLine("File updated successfully.");
                            }
                            else
                            {
                                Console.WriteLine("Failed to write file.");
                            }
                        }
                        else
                        {
                            Console.WriteLine("Edit cancelled.");
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"\nEdit request failed: {ex.Message}");
                    }

                    continue;
                }

                chatHistory.Add(("user", input));

                try
                {
                    var response = await modelService.GetResponseAsync(chatHistory);
                    chatHistory.Add(("assistant", response));
                    Console.WriteLine($"\nAssistant: {response}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"\nError: {ex.Message}");
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
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"✓ Foundry Local ready: {modelService.ModelName}");
        Console.ResetColor();
        Console.WriteLine($"  Endpoint: {runtimeInfo.Endpoint}");
        Console.WriteLine($"  Accelerator: {runtimeInfo.AcceleratorSummary}");
        Console.WriteLine($"  NPU detected: {(runtimeInfo.IsNpu ? "yes" : "no / unknown")}");
    }

    static async Task PrintSelectableModelsAsync(LocalModelService modelService, int? maxToShow = null)
    {
        try
        {
            var models = await modelService.ListAvailableModelsAsync();
            if (models.Count == 0)
            {
                Console.WriteLine("\nFoundry Local did not report any selectable models.");
                return;
            }

            Console.WriteLine($"\nSelectable models ({models.Count}):");
            foreach (var model in maxToShow is int limit ? models.Take(limit) : models)
            {
                Console.WriteLine($"  {model}");
            }

            if (maxToShow is int visibleCount && models.Count > visibleCount)
            {
                Console.WriteLine($"  ... and {models.Count - visibleCount} more");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\nModel listing failed: {ex.Message}");
        }
    }

    static void PrintHelp()
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("Available Commands:");
        Console.ResetColor();
        Console.WriteLine("  <message>       - Chat with the coding assistant about your code");
        Console.WriteLine("  /list          - List all source files in the workspace");
        Console.WriteLine("  /read <file>   - Read and display a specific file");
        Console.WriteLine("  /status        - Show the active model and local runtime details");
        Console.WriteLine("  /ping          - Run a quick model smoke test");
        Console.WriteLine("  /models        - List selectable Foundry Local aliases and variant IDs");
        Console.WriteLine("  /use <model>   - Switch to a specific Foundry Local model alias or ID");
        Console.WriteLine("  /edit <file>   - Edit a file with AI assistance (creates .backup)");
        Console.WriteLine("  help           - Show this help message");
        Console.WriteLine("  exit           - Exit the application");
        Console.WriteLine();
        Console.WriteLine("Example workflow:");
        Console.WriteLine("  > /list");
        Console.WriteLine("  > /read Program.cs");
        Console.WriteLine("  > /ping");
        Console.WriteLine("  > /edit Program.cs");
        Console.WriteLine("  > Add error handling to the Main method");
    }

    static void PrintEditPreview(string currentContent, string proposedContent)
    {
        Console.WriteLine("\n--- Proposed Changes ---");
        Console.WriteLine($"Current size:  {currentContent.Length} characters");
        Console.WriteLine($"Proposed size: {proposedContent.Length} characters");
        Console.WriteLine();

        var previewLines = proposedContent
            .Split(Environment.NewLine)
            .Take(40)
            .ToList();

        foreach (var line in previewLines)
        {
            Console.WriteLine(line);
        }

        var totalLines = proposedContent.Split(Environment.NewLine).Length;
        if (totalLines > previewLines.Count)
        {
            Console.WriteLine($"... ({totalLines - previewLines.Count} more lines)");
        }

        Console.WriteLine("--- End ---");
    }

    static void PrintSetupGuide()
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("Setup Guide:");
        Console.ResetColor();
        Console.WriteLine();
        Console.WriteLine("This application uses Foundry Local to start a model and use either its local web endpoint or the in-process SDK client.");
        Console.WriteLine();
        Console.WriteLine("Steps to set up:");
        Console.WriteLine("  1. Restore the project so the Foundry Local SDK packages are installed.");
        Console.WriteLine();
        Console.WriteLine("  2. Ensure Foundry Local can access a supported local model catalog.");
        Console.WriteLine();
        Console.WriteLine("  3. Optionally set the default model before running:");
        Console.WriteLine("     - FOUNDRY_LOCAL_MODEL=phi-3-mini-128k-instruct-qnn-npu:4");
        Console.WriteLine();
        Console.WriteLine("  4. Run /models to inspect every selectable alias and variant ID from the local catalog.");
        Console.WriteLine();
        Console.WriteLine("  5. Use /use <model> to switch to the exact model variant you want to run.");
        Console.WriteLine();
        Console.WriteLine("  6. Use /status to confirm the active model and whether Foundry Local exposed a web endpoint or is using the in-process SDK client.");
        Console.WriteLine();
        Console.WriteLine("For more information, visit:");
        Console.WriteLine("  https://learn.microsoft.com/en-us/azure/foundry-local/get-started");
    }
}
